using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Rug.Osc;
using ResoniteModLoader;

namespace BaballoniaEyeExpressions;

public class BFRLink(Action<OscMessage> onOscMessageReceived) {
	private UdpClient? _udp;
	private CancellationTokenSource? _cancellationToken;
	private readonly string _moduleId = Guid.NewGuid().ToString("N")[..16];

	private string? _activeSessionId = null;
	private long _packetsReceived = 0;
	private DateTime _lastSeen = DateTime.MinValue;

	private readonly Action<OscMessage> _onOscMessageReceived = onOscMessageReceived;

	public void Start() {
		_udp = new UdpClient();

		_udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

		_udp.EnableBroadcast = true;
		_udp.Client.Bind(new IPEndPoint(IPAddress.Any, 6767));

		_cancellationToken = new CancellationTokenSource();
		Task.Run(() => ListenLoop(_cancellationToken.Token));

		BroadcastQuery();
	}

	public void Stop() {
		if (_activeSessionId != null && _udp != null) {
			var bye = new { proto = "bfr-link", v = 1, type = "bye", mid = _moduleId };
			SendUdp(bye, new IPEndPoint(IPAddress.Broadcast, 6767));
		}

		_cancellationToken?.Cancel();
		_cancellationToken?.Dispose();
		_udp?.Close();
	}

	private void BroadcastQuery() {
		var query = new { proto = "bfr-link", v = 1, type = "query", mid = _moduleId };
		SendUdp(query, new IPEndPoint(IPAddress.Broadcast, 6767));
	}

	private async Task ListenLoop(CancellationToken token) {
		while (!token.IsCancellationRequested && _udp != null) {
			try {
				var result = await _udp.ReceiveAsync(token);
				byte[] data = result.Buffer;

				if (data.Length > 0 && data[0] == '{') {
					HandleJsonControl(data, result.RemoteEndPoint);
				} else {
					HandleOscData(data);
				}
			} catch (SocketException) {
			} catch (ObjectDisposedException) {
				break;
			} catch (OperationCanceledException) {
				break;
			}
		}
	}

	private void HandleJsonControl(byte[] data, IPEndPoint sender) {
		try {
			using var doc = JsonDocument.Parse(data);
			var root = doc.RootElement;

			if (!root.TryGetProperty("proto", out var protoProp) || protoProp.GetString() != "bfr-link") return;

			string? type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
			string? sid = root.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;

			if (type == "beacon") {
				if (_activeSessionId != null && _activeSessionId != sid) {
					if ((DateTime.UtcNow - _lastSeen).TotalSeconds > 3) {
						_activeSessionId = null;
					}
				}

				bool isNewSession = (_activeSessionId == null);
				_activeSessionId ??= sid;
				bool isBusy = (_activeSessionId != sid);

				if (isNewSession && !isBusy) {
					BaballoniaEyeExpressions.Msg($"Sending an offer to connect to {sid ?? "(Unknown Session ID)"}!");
				}

				var offer = new {
					proto = "bfr-link",
					v = 1,
					type = "offer",
					mid = _moduleId,
					name = "Resonite",
					busy = isBusy,
					eye = true,
					face = true,
					restart = false
				};

				SendUdp(offer, sender);
				if (!isBusy) _lastSeen = DateTime.UtcNow;

			} else if (type == "ping" && sid == _activeSessionId) {
				_lastSeen = DateTime.UtcNow;

				object? seqVal = null;
				if (root.TryGetProperty("seq", out var seqElement) && seqElement.ValueKind == JsonValueKind.Number) {
					seqVal = seqElement.GetInt64();
				}

				var pong = new {
					proto = "bfr-link",
					v = 1,
					type = "pong",
					mid = _moduleId,
					seq = seqVal,
					rx = _packetsReceived,
					eye = true,
					face = true,
					restart = false
				};

				SendUdp(pong, sender);
			} else if (type == "bye" && sid == _activeSessionId) {
				_activeSessionId = null;
				BaballoniaEyeExpressions.Msg("Disconnected from Babble Frame Runtime.");
			}
		} catch (Exception) {

		}
	}

	private void HandleOscData(byte[] data) {
		if (_activeSessionId == null) return;

		_lastSeen = DateTime.UtcNow;
		_packetsReceived++;

		try {
			var packet = OscPacket.Read(data, data.Length);
			if (packet is OscMessage msg) {
				_onOscMessageReceived?.Invoke(msg);
			} else if (packet is OscBundle bundle) {
				foreach (var item in bundle) {
					if (item is OscMessage m) _onOscMessageReceived?.Invoke(m);
				}
			}
		} catch (Exception ex) {
			ResoniteMod.Error($"OSC Parse Error: {ex.Message}");
		}
	}

	private void SendUdp(object obj, IPEndPoint target) {
		try {
			byte[] payload = JsonSerializer.SerializeToUtf8Bytes(obj);
			_udp?.Send(payload, payload.Length, target);
		} catch (Exception) {

		}
	}
}
