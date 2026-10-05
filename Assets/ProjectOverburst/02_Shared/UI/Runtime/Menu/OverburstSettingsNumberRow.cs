using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// Integer slider ticks keep mouse drag, arrows and buttons on the same 0.1 grid.
public sealed class OverburstSettingsNumberRow : MonoBehaviour
{
    public BloodComparisonTuning.Control control;
    public Button decrease, increase;
    public Slider slider;
    public Text valueLabel;
    bool refreshing;
    public void Bind()
    {
        decrease.onClick.AddListener(() => Adjust(-1));
        increase.onClick.AddListener(() => Adjust(1));
        slider.onValueChanged.AddListener(ticks => { if (!refreshing) { BloodComparisonTuning.Set(control, ticks / 10f); RefreshValue(); } });
        RefreshValue();
    }
    void Adjust(int steps) { BloodComparisonTuning.Adjust(control, steps); RefreshValue(); }
    public void RefreshValue()
    {
        refreshing = true;
        float value = BloodComparisonTuning.Value(control);
        slider.SetValueWithoutNotify(Mathf.Round(value * 10f));
        valueLabel.text = value.ToString("0.0", CultureInfo.InvariantCulture);
        decrease.interactable = value > BloodComparisonTuning.Minimum(control) + .001f;
        increase.interactable = value < BloodComparisonTuning.Maximum(control) - .001f;
        refreshing = false;
    }
}
