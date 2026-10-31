using System.Reflection;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using Rug.Osc;

namespace BaballoniaEyeExpressions;
//More info on creating mods can be found https://github.com/resonite-modding-group/ResoniteModLoader/wiki/Creating-Mods
public class BaballoniaEyeExpressions : ResoniteMod {
	internal const string VERSION_CONSTANT = "2.1.1"; //Changing the version here updates it in all locations needed
	public override string Name => "BaballoniaEyeExpressions";
	public override string Author => "Noble";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/noblereign/BaballoniaEyeExpressions/";

	private static ModConfiguration? _config;

	[AutoRegisterConfigKey]
	private static readonly ModConfigurationKey<bool> UseBabbleLink = new("Use BabbleLink", "Connect directly to Babble Frame Runtime using its discovery protocol", () => false);

	[AutoRegisterConfigKey]
	private static readonly ModConfigurationKey<bool> RaiseDilationRange = new("Raised dilation range", "Raises the upper limit for dilation to 8mm.\nResonite's official implementation only goes up to 6mm, this option is provided in case you preferred the old modded range.", () => false);

	// instead of using PositiveInfinity like the real code does, I'm just initializing with 0f
	// would rather the mod do nothing if you're not actually using the expressions build lol
	private static float leftEyeBrow = 0f;
	private static float leftEyeSquint = 0f;
	
	private static float rightEyeBrow = 0f;
	private static float rightEyeSquint = 0f;
	
	private static BFRLink? _bfrLink;
	private static BabbleOSC_Driver? _activeDriver;
	private static MethodInfo? _updateDataMethod;

	public override void OnEngineInit() {
		_config = GetConfiguration()!;
		_config.OnThisConfigurationChanged += OnConfigurationChanged;

		Harmony harmony = new("dog.glacier.BaballoniaEyeExpressions");
		harmony.PatchAll();

		_updateDataMethod = AccessTools.Method(typeof(BabbleOSC_Driver), "UpdateData");

		if (_updateDataMethod == null) {
			Error("Couldn't find BabbleOSC_Driver.UpdateData! BFRLink can't run!");
			return;
		}

		_bfrLink = new BFRLink(ProxyOscMessage);

		if (_config.GetValue(UseBabbleLink)) {
			_bfrLink.Start();
			Msg($"Listening for Babble Frame Runtime on port 6767");
		}

		Engine.Current.OnShutdown += OnEngineShutdown;
	}

	private static void OnEngineShutdown() {
		Engine.Current.RegisterShutdownTask(Task.Run(async () => {
			Msg("Shutting down BFRLink...");
			_bfrLink?.Stop();
			await Task.Delay(250);
		}));
	}

	private static void OnConfigurationChanged(ConfigurationChangedEvent @event) {
		if (@event.Key == UseBabbleLink) {
			if (_config!.GetValue(UseBabbleLink)) {
				_bfrLink?.Start();
				Msg($"Listening for Babble Frame Runtime on port 6767");
			} else {
				Msg("Shutting down BFRLink...");
				_bfrLink?.Stop();
			}
		}
	}

	private static void ProxyOscMessage(OscMessage message) {
		if (_activeDriver == null || _updateDataMethod == null) return;

		try {
			_updateDataMethod.Invoke(_activeDriver, [message]);
		} catch (Exception ex) {
			Error($"Failed to invoke UpdateData: {ex}");
		}
	}

	[HarmonyPatch(typeof(BabbleOSC_Driver), "UpdateData")]
	class BabbleOSC_Driver_UpdateData_Patch {
		static void Postfix(BabbleOSC_Driver __instance, OscMessage message,
			ref float ___leftEyeX,
			ref float ___leftEyeY,
			ref float ___leftEyeLid,
			ref float ___leftEyeWiden,
			ref float ___leftEyeDilation,
			ref float ___rightEyeX,
			ref float ___rightEyeY,
			ref float ___rightEyeLid,
			ref float ___rightEyeWiden,
			ref float ___rightEyeDilation) {

			string address = message.Address;
			if (address == null) {
				return;
			}

			switch (address) {
				case "/leftEyeLid":
					___leftEyeLid = OSC_Driver.ReadFloat(message);
					break;
				case "/leftEyeWiden":
					___leftEyeWiden = OSC_Driver.ReadFloat(message);
					break;
				case "/leftEyeDilation":
					___leftEyeDilation = OSC_Driver.ReadFloat(message);
					break;
				case "/leftEyeX":
					___leftEyeX = OSC_Driver.ReadFloat(message);
					break;
				case "/leftEyeY":
					___leftEyeY = OSC_Driver.ReadFloat(message);
					break;
				case "/rightEyeLid":
					___rightEyeLid = OSC_Driver.ReadFloat(message);
					break;
				case "/rightEyeWiden":
					___rightEyeWiden = OSC_Driver.ReadFloat(message);
					break;
				case "/rightEyeDilation":
					___rightEyeDilation = OSC_Driver.ReadFloat(message);
					break;
				case "/rightEyeX":
					___rightEyeX = OSC_Driver.ReadFloat(message);
					break;
				case "/rightEyeY":
					___rightEyeY = OSC_Driver.ReadFloat(message);
					break;
				// All of the above are catching the lowercase version of already supported addresses, because I think they might have switched from PascalCase to camelCase
				case "/LeftEyeBrow":
				case "/leftEyeBrow":
					leftEyeBrow = OSC_Driver.ReadFloat(message);
					break;
				case "/LeftEyeSquint":
				case "/leftEyeSquint":
					leftEyeSquint = OSC_Driver.ReadFloat(message);
					break;
				case "/RightEyeBrow":
				case "/rightEyeBrow":
					rightEyeBrow = OSC_Driver.ReadFloat(message);
					break;
				case "/RightEyeSquint":
				case "/rightEyeSquint":
					rightEyeSquint = OSC_Driver.ReadFloat(message);
					break;
			}
		}
	}

	[HarmonyPatch(typeof(BabbleOSC_Driver), "UpdateEyes")]
	class BabbleOSC_Driver_UpdateEyes_Patch {
		private static bool IsTracking(DateTime? timestamp) {
			if (!timestamp.HasValue) {
				return false;
			}
			if ((DateTime.UtcNow - timestamp.Value).TotalSeconds > 10.0) {
				return false;
			}
			return true;
		}

		static bool Prefix(
			BabbleOSC_Driver __instance, 
			Eyes ___eyes,
			DateTime? ___lastMessageTime,
			float ___leftEyeX,
			float ___leftEyeY,
			float ___leftEyeLid,
			float ___leftEyeWiden,
			float ___leftEyeDilation,
			float ___rightEyeX,
			float ___rightEyeY,
			float ___rightEyeLid,
			float ___rightEyeWiden,
			float ___rightEyeDilation) {

			if (_activeDriver != __instance) {
				_activeDriver = __instance;
			}

			bool isTracking = IsTracking(___lastMessageTime);

			// recreate this cause even though its in the game its not something easily accessible with harmony
			bool receivedFullEyeData = !(float.IsInfinity(___leftEyeX) || float.IsInfinity(___leftEyeY) || float.IsInfinity(___leftEyeLid) ||
			                           float.IsInfinity(___rightEyeX) || float.IsInfinity(___rightEyeY) || float.IsInfinity(___rightEyeLid));

			bool receivedEyeDilation = !float.IsInfinity(___leftEyeDilation) && !float.IsInfinity(___rightEyeDilation);

			bool altDilation = _config?.GetValue(RaiseDilationRange) ?? false;

			___eyes.SupportsPupilTracking = receivedEyeDilation;

			if (!isTracking || !receivedFullEyeData)
			{
				___eyes.IsEyeTrackingActive = false;
				___eyes.IsTracking = false;
				return false;
			}

			

			___eyes.IsTracking = true;
			___eyes.IsEyeTrackingActive = true;

			UpdateEye(___eyes.LeftEye, ___leftEyeX, ___leftEyeY, ___leftEyeLid, ___leftEyeWiden, leftEyeSquint, leftEyeBrow, ___leftEyeDilation, receivedEyeDilation, altDilation);
			UpdateEye(___eyes.RightEye, ___rightEyeX, ___rightEyeY, ___rightEyeLid, ___rightEyeWiden, rightEyeSquint, rightEyeBrow, ___rightEyeDilation, receivedEyeDilation, altDilation);

			static void UpdateEye(Eye eye, float x, float y, float lid, float wide, float squint, float brow, float diameter, bool receivedEyeDilation, bool altDilation) {
				float z = MathX.Sqrt(1f - (x * x + y * y));
				eye.UpdateWithDirection(new float3(x, y, z).Normalized);
				eye.Openness = lid;
				eye.Widen = wide;
				eye.Squeeze = squint;
				eye.InnerBrowVertical = -brow; // Baballonia maps brow to BrowLowerer, so invert the received value since resonite does +1 to -1
				if (receivedEyeDilation) {
					eye.PupilDiameter = MathX.Lerp(0.002f, altDilation ? 0.008f : 0.006f, diameter);
				}
			}
			return false;
		}
	}
}
