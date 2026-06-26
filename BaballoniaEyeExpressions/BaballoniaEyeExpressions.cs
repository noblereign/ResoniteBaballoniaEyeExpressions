using Elements.Core;

using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;

using Rug.Osc;

namespace BaballoniaEyeExpressions;
//More info on creating mods can be found https://github.com/resonite-modding-group/ResoniteModLoader/wiki/Creating-Mods
public class BaballoniaEyeExpressions : ResoniteMod {
	internal const string VERSION_CONSTANT = "1.0.0"; //Changing the version here updates it in all locations needed
	public override string Name => "BaballoniaEyeExpressions";
	public override string Author => "Noble";
	public override string Version => VERSION_CONSTANT;
	public override string Link => "https://github.com/noblereign/BaballoniaEyeExpressions/";

	// instead of using PositiveInfinity like the real code does, I'm just initializing with 0f
	// would rather the mod do nothing if you're not actually using the expressions build lol
	public static float leftEyeWiden = 0f;
	public static float leftEyeBrow = 0f;
	public static float leftEyeSquint = 0f;
	public static float leftEyeLower = 0f; // don't think lower is actually used yet, but just covering my bases

	public static float rightEyeWiden = 0f;
	public static float rightEyeBrow = 0f;
	public static float rightEyeSquint = 0f;
	public static float rightEyeLower = 0f; // don't think lower is actually used yet, but just covering my bases


	public override void OnEngineInit() {
		Harmony harmony = new("dog.glacier.BaballoniaEyeExpressions");
		harmony.PatchAll();
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
				case "/LeftEyeLower":
				case "/leftEyeLower":
					leftEyeLower = OSC_Driver.ReadFloat(message);
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
				case "/RightEyeLower":
				case "/rightEyeLower":
					rightEyeLower = OSC_Driver.ReadFloat(message);
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

			UpdateEye(___eyes.LeftEye, ___leftEyeX, ___leftEyeY, ___leftEyeLid, leftEyeWiden, leftEyeSquint, leftEyeBrow, leftEyeLower);
			UpdateEye(___eyes.RightEye, ___rightEyeX, ___rightEyeY, ___rightEyeLid, rightEyeWiden, rightEyeSquint, rightEyeBrow, rightEyeLower);

			void UpdateEye(Eye eye, float x, float y, float lid, float wide, float squint, float brow, float lower) {
				float z = MathX.Sqrt(1f - (x * x + y * y));
				eye.UpdateWithDirection(new float3(x, y, z).Normalized);
				eye.Openness = lid;
				eye.Widen = wide;
				eye.Squeeze = squint;
				eye.InnerBrowVertical = brow;
				// literally what was lower used for tho 🤔
			}
			return false;
		}
	}
}
