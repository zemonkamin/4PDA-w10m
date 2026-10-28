using System;

namespace _4PDA.Services
{
    internal enum ScrollJumpAction { None, Hide, Up, Down }

    internal sealed class ScrollJumpState
    {
        private double _offset, _extent, _distance;
        private int _direction;

        internal void Reset(double offset = 0, double extent = 0)
        {
            _offset = offset; _extent = extent; _distance = 0; _direction = 0;
        }

        internal ScrollJumpAction Observe(double offset, double extent, bool enabled, bool programmatic)
        {
            double delta = offset - _offset;
            bool resized = Math.Abs(extent - _extent) > 1;
            _offset = offset; _extent = extent;
            if (!enabled || programmatic || extent <= 4 || offset <= 2 || extent - offset <= 2 || resized)
            {
                _distance = 0; _direction = 0;
                return ScrollJumpAction.Hide;
            }
            if (Math.Abs(delta) < 0.5) return ScrollJumpAction.None;
            int direction = delta < 0 ? -1 : 1;
            if (direction != _direction) { _distance = 0; _direction = direction; }
            _distance += Math.Abs(delta);
            if (_distance < 32) return ScrollJumpAction.None;
            _distance = 0;
            return direction < 0 ? ScrollJumpAction.Up : ScrollJumpAction.Down;
        }
    }
}
