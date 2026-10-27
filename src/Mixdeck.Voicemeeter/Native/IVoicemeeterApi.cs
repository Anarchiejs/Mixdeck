namespace Mixdeck.Voicemeeter.Native;

public interface IVoicemeeterApi
{
    int Login();
    int Logout();
    int RunVoicemeeter(int type);
    int GetVoicemeeterType(out int type);
    int GetVoicemeeterVersion(out int version);
    int IsParametersDirty();
    int GetParameterFloat(string name, out float value);
    int GetParameterString(string name, out string value);
    int SetParameterFloat(string name, float value);
    int SetParameterString(string name, string value);
    int SetParameters(string script);
    int GetLevel(int type, int channel, out float value);
}
