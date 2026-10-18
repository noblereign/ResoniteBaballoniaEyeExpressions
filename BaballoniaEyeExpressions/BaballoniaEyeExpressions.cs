using System.Reflection;
using System.Reflection.Emit;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using Rug.Osc;

namespace BaballoniaEyeExpressions;
//More info on creating mods can be found https://github.com/resonite-modding-group/ResoniteModLoader/wiki/Creating-Mods
public class BaballoniaEyeExpressions : ResoniteMod {
	internal const string VERSION_CONSTANT = "2.0.0"; //Changing the version here updates it in all locations needed
	public override string Name => "BaballoniaEyeExpressions";
	public override string Author => "Noble";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/noblereign/BaballoniaEyeExpressions/";

	// instead of using PositiveInfinity like the real code does, I'm just initializing with 0f
	// would rather the mod do nothing if you're not actually using the expressions build lol
	private static float leftEyeWiden = 0f;
	private static float leftEyeBrow = 0f;
	private static float leftEyeSquint = 0f;
	private static float leftEyePupilDiameter = 4f;

	private static float rightEyeWiden = 0f;
	private static float rightEyeBrow = 0f;
	private static float rightEyeSquint = 0f;
	private static float rightEyePupilDiameter = 4f;

	private static BFRLink? _bfrLink;
	private static BabbleOSC_Driver? _activeDriver;
	private static MethodInfo? _updateDataMethod;

	public override void OnEngineInit() {
		Harmony harmony = new("dog.glacier.BaballoniaEyeExpressions");
		harmony.PatchAll();

		_updateDataMethod = AccessTools.Method(typeof(BabbleOSC_Driver), "UpdateData");

		if (_updateDataMethod == null) {
			Error("Couldn't find BabbleOSC_Driver.UpdateData! BFRLink can't run!");
			return;
		}

		_bfrLink = new BFRLink(ProxyOscMessage);
		_bfrLink.Start();
		Engine.Current.OnShutdown += OnEngineShutdown;
		Msg($"Listening for Babble Frame Runtime on port 6767");
	}

	private static void OnEngineShutdown() {
		Engine.Current.RegisterShutdownTask(Task.Run(async () => {
			Msg("Shutting down BFRLink...");
			_bfrLink?.Stop();
			await Task.Delay(250);
		}));
	}

	private static void ProxyOscMessage(OscMessage message) {
		if (_activeDriver == null || _updateDataMethod == null) return;

		try {
			_updateDataMethod.Invoke(_activeDriver, [message]);
		} catch (Exception ex) {
			Error($"Failed to invoke UpdateData: {ex}");
		}
	}

	[HarmonyPatch(typeof(BabbleOSC_Driver), "RegisterInputs")] // tell resonite there's pupil tracking (because of BFR)
	class BabbleOSC_Driver_RegisterInputs_Patch {
		private static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
			var codes = new List<CodeInstruction>(instructions);

			for (int i = 0; i < codes.Count; i++) {
				if (codes[i].opcode == OpCodes.Newobj &&
					codes[i].operand is System.Reflection.ConstructorInfo ctor &&
					ctor.DeclaringType == typeof(Eyes)) { // frooxengine.eyes created
					if (codes[i - 1].opcode == OpCodes.Ldc_I4_0) { // pupil tracking false
						codes[i - 1].opcode = OpCodes.Ldc_I4_1; // become pupil tracking true 👀
						break;
					}
				}
			}
			return codes;
		}
	}

	[HarmonyPatch(typeof(BabbleOSC_Driver), "UpdateData")]
	class BabbleOSC_Driver_UpdateData_Patch {
		static void Postfix(BabbleOSC_Driver __instance, OscMessage message,
			ref float ___leftEyeX,
			ref float ___leftEyeY,
			ref float ___leftEyeLid,
			ref float ___rightEyeX,
			ref float ___rightEyeY,
			ref float ___rightEyeLid) {

			string address = message.Address;
			if (address == null) {
				return;
			}

			switch (address) {
				case "/leftEyeLid":
					___leftEyeLid = OSC_Driver.ReadFloat(message);
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
				case "/rightEyeX":
					___rightEyeX = OSC_Driver.ReadFloat(message);
					break;
				case "/rightEyeY":
					___rightEyeY = OSC_Driver.ReadFloat(message);
					break;
					// All of the above are catching the lowercase version of already supported addresses, because I think they might have switched from PascalCase to camelCase
				case "/LeftEyeWiden":
				case "/leftEyeWiden":
					leftEyeWiden = OSC_Driver.ReadFloat(message);
					break;
				case "/LeftEyeBrow":
				case "/leftEyeBrow":
					leftEyeBrow = OSC_Driver.ReadFloat(message);
					break;
				case "/LeftEyeSquint":
				case "/leftEyeSquint":
					leftEyeSquint = OSC_Driver.ReadFloat(message);
					break;
				case "/LeftEyeDilation":
				case "/leftEyeDilation":
					leftEyePupilDiameter = MathX.Remap(MathX.Clamp01(OSC_Driver.ReadFloat(message)), 0, 1, 2, 8);
					break;
				case "/RightEyeWiden":
				case "/rightEyeWiden":
					rightEyeWiden = OSC_Driver.ReadFloat(message);
					break;
				case "/RightEyeBrow":
				case "/rightEyeBrow":
					rightEyeBrow = OSC_Driver.ReadFloat(message);
					break;
				case "/RightEyeSquint":
				case "/rightEyeSquint":
					rightEyeSquint = OSC_Driver.ReadFloat(message);
					break;
				case "/RightEyeDilation":
				case "/rightEyeDilation":
					rightEyePupilDiameter = MathX.Remap(MathX.Clamp01(OSC_Driver.ReadFloat(message)), 0, 1, 2, 8);
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
			float ___rightEyeX,
			float ___rightEyeY,
			float ___rightEyeLid) {

			if (_activeDriver != __instance) {
				_activeDriver = __instance;
			}

			bool isTracking = IsTracking(___lastMessageTime);

			// recreate this cause even though its in the game its not something easily accessible with harmony
			bool receivedFullEyeData = !(float.IsInfinity(___leftEyeX) || float.IsInfinity(___leftEyeY) || float.IsInfinity(___leftEyeLid) ||
			                           float.IsInfinity(___rightEyeX) || float.IsInfinity(___rightEyeY) || float.IsInfinity(___rightEyeLid));

			
			if (!isTracking || !receivedFullEyeData)
			{
				___eyes.IsEyeTrackingActive = false;
				___eyes.IsTracking = false;
				return false;
			}

			

			___eyes.IsTracking = true;
			___eyes.IsEyeTrackingActive = true;

			UpdateEye(___eyes.LeftEye, ___leftEyeX, ___leftEyeY, ___leftEyeLid, leftEyeWiden, leftEyeSquint, leftEyeBrow, leftEyePupilDiameter);
			UpdateEye(___eyes.RightEye, ___rightEyeX, ___rightEyeY, ___rightEyeLid, rightEyeWiden, rightEyeSquint, rightEyeBrow, rightEyePupilDiameter);

			static void UpdateEye(Eye eye, float x, float y, float lid, float wide, float squint, float brow, float diameter) {
				float z = MathX.Sqrt(1f - (x * x + y * y));
				eye.UpdateWithDirection(new float3(x, y, z).Normalized);
				eye.Openness = lid;
				eye.Widen = wide;
				eye.Squeeze = squint;
				eye.InnerBrowVertical = -brow; // Baballonia maps brow to BrowLowerer, so invert the received value since resonite does +1 to -1
				eye.PupilDiameter = diameter * 0.001f; // Resonite seems to multiply this by 0.001 in the official drivers, so...
			}
			return false;
		}
	}
}
