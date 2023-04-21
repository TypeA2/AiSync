using System;
using System.Reflection;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;

namespace AiSyncClient {
    public class AiSlider : Slider {

        private ToolTip? _tooltip;
        public ToolTip AutoToolTip {
            get {
                if (_tooltip == null) {
                    FieldInfo? field = typeof(Slider).GetField("_autoToolTip", BindingFlags.NonPublic | BindingFlags.Instance);

                    if (field is null || !field.FieldType.IsAssignableTo(typeof(ToolTip))) {
                        throw new InvalidOperationException("_autoToolTip field not found");
                    }

                    _tooltip = (ToolTip)(field.GetValue(this) ?? throw new InvalidOperationException("_autoToolTip failure"));
                }

                return _tooltip;
            }
        }

        public delegate string ToolTipFormatter(double value);

        public ToolTipFormatter? Formatter { get; set; }

        protected override void OnThumbDragStarted(DragStartedEventArgs e) {
            base.OnThumbDragStarted(e);

            UpdateTooltip();
        }

        protected override void OnThumbDragDelta(DragDeltaEventArgs e) {
            base.OnThumbDragDelta(e);

            UpdateTooltip();
        }

        private void UpdateTooltip() {
            if (Formatter is null) {
                AutoToolTip.Content = Value.ToString();
            } else {
                AutoToolTip.Content = Formatter(Value);
            }
        }
    }
}
