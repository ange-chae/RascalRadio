using Godot;

public partial class AudioSettingsManager : Node
{
    public const string BgmBusName = "BGM";
    public const string SfxBusName = "SFX";

    public static AudioSettingsManager Instance { get; private set; }

    private int _masterBusIndex = -1;
    private int _bgmBusIndex = -1;
    private int _sfxBusIndex = -1;

    public float MasterVolumePercent { get; private set; } = 100.0f;
    public bool BgmMuted { get; private set; }
    public bool SfxMuted { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void _Ready()
    {
        _masterBusIndex = AudioServer.GetBusIndex("Master");
        if (_masterBusIndex < 0)
        {
            GD.PushError("[AudioSettings] The Master audio bus was not found.");
            return;
        }

        _bgmBusIndex = GetOrCreateBus(BgmBusName);
        _sfxBusIndex = GetOrCreateBus(SfxBusName);

        SetMasterVolumePercent(100.0f);
        SetBgmMuted(false);
        SetSfxMuted(false);
    }

    public void SetMasterVolumePercent(double percent)
    {
        MasterVolumePercent = Mathf.Clamp((float)percent, 0.0f, 100.0f);
        if (_masterBusIndex >= 0)
        {
            AudioServer.SetBusVolumeLinear(_masterBusIndex, MasterVolumePercent / 100.0f);
        }
    }

    public void SetBgmMuted(bool muted)
    {
        BgmMuted = muted;
        if (_bgmBusIndex >= 0)
        {
            AudioServer.SetBusMute(_bgmBusIndex, muted);
        }
    }

    public void SetSfxMuted(bool muted)
    {
        SfxMuted = muted;
        if (_sfxBusIndex >= 0)
        {
            AudioServer.SetBusMute(_sfxBusIndex, muted);
        }
    }

    private static int GetOrCreateBus(string name)
    {
        int busIndex = AudioServer.GetBusIndex(name);
        if (busIndex < 0)
        {
            AudioServer.AddBus();
            busIndex = AudioServer.BusCount - 1;
            AudioServer.SetBusName(busIndex, name);
        }

        AudioServer.SetBusSend(busIndex, "Master");
        return busIndex;
    }
}
