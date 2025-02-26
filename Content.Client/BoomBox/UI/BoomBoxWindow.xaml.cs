using System.Linq;
using Content.Shared.BoomBox;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.BoomBox.UI;

[GenerateTypedNameReferences]
public sealed partial class BoomBoxWindow : DefaultWindow
{
    public event Action? MinusVolButtonPressed;
    public event Action? PlusVolButtonPressed;
    public event Action? StartButtonPressed;
    public event Action? StopButtonPressed;
    public event Action<float>? PlaybackSliderChanged;

    private bool _isSliderDragging = false;

    public BoomBoxWindow()
    {
        RobustXamlLoader.Load(this);

        MinusVolButton.OnPressed += _ => MinusVolButtonPressed?.Invoke();
        PlusVolButton.OnPressed += _ => PlusVolButtonPressed?.Invoke();
        StartButton.OnPressed += _ => StartButtonPressed?.Invoke();
        StopButton.OnPressed += _ => StopButtonPressed?.Invoke();
        PlaybackSlider.OnValueChanged += args => PlaybackSliderChanged?.Invoke(args.Value);
    }

    public void UpdateState(BoomBoxUiState state)
    {
        MinusVolButton.Disabled = !state.CanMinusVol;
        PlusVolButton.Disabled = !state.CanPlusVol;
        StartButton.Disabled = !state.CanStart;
        StopButton.Disabled = !state.CanStop;

        PlaybackSlider.MaxValue = state.SoundDuration;
        PlaybackSlider.SetValueWithoutEvent(state.PlaybackPosition);
    }
}
