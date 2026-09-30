using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace WarThunderChatTranslator.Controls
{
    /// <summary>
    /// Transparent resize region that uses WinUI's protected cursor pipeline instead of calling user32
    /// SetCursor from pointer events. Border is sealed in WinUI 3, so this helper derives from Grid,
    /// which still provides Background/Width/Height/alignment and pointer handling for our hit areas.
    /// </summary>
    public sealed class CursorBorder : Grid
    {
        private InputCursor _ownedCursor;
        private InputSystemCursorShape _cursorShape = InputSystemCursorShape.Arrow;

        public InputSystemCursorShape CursorShape
        {
            get => _cursorShape;
            set
            {
                _cursorShape = value;
                ApplyCursor(value);
            }
        }

        private void ApplyCursor(InputSystemCursorShape shape)
        {
            if (_ownedCursor is InputSystemCursor current && current.CursorShape == shape)
            {
                return;
            }

            var next = InputSystemCursor.Create(shape);
            ProtectedCursor = next;

            _ownedCursor?.Dispose();
            _ownedCursor = next;
        }
    }

    /// <summary>
    /// Grid variant used by the draggable overlay toolbar.
    /// </summary>
    public sealed class CursorGrid : Grid
    {
        private InputCursor _ownedCursor;
        private InputSystemCursorShape? _cursorShape;

        public void SetSystemCursor(InputSystemCursorShape? shape)
        {
            if (_cursorShape == shape)
            {
                return;
            }

            var previous = _ownedCursor;
            _ownedCursor = null;
            _cursorShape = shape;

            if (shape.HasValue)
            {
                var next = InputSystemCursor.Create(shape.Value);
                ProtectedCursor = next;
                _ownedCursor = next;
            }
            else
            {
                ProtectedCursor = null;
            }

            previous?.Dispose();
        }
    }

    /// <summary>
    /// Keeps interactive toolbar buttons on the normal arrow when the parent toolbar
    /// uses the move cursor in adjustment mode.
    /// </summary>
    public sealed class ArrowCursorButton : Button
    {
        private readonly InputCursor _cursor;

        public ArrowCursorButton()
        {
            _cursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            ProtectedCursor = _cursor;
        }
    }
}
