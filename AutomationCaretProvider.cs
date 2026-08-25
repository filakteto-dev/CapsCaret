using System.Collections.Concurrent;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace CapsCaret;

internal sealed class AutomationCaretProvider : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    private volatile CaretPosition? _latestPosition;
    
    public void Invalidate()
    {
        _latestPosition = null;
    }
    public AutomationCaretProvider()
    {
        _thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "CapsCaret UI Automation"
        };

        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void RequestUpdate()
    {
        if (_queue.Count > 0)
            return;

        _queue.Add(UpdateCaretPosition);
    }

    public bool TryGetLatestPosition(
        out int x,
        out int y)
    {
        var position = _latestPosition;

        if (position is null)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = position.X;
        y = position.Y;

        return true;
    }

    private void WorkerLoop()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch
            {
                _latestPosition = null;
            }
        }
    }

    private void UpdateCaretPosition()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            
            if (element is null)
            {
                _latestPosition = null;
                return;
            }

            if (!element.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out var patternObject))
            {
                _latestPosition = null;
                return;
            }

            var pattern = (TextPattern)patternObject;

            var selections = pattern.GetSelection();

            if (selections.Length == 0)
            {
                _latestPosition = null;
                return;
            }

            var caretRange = selections[0];

            if (TryGetPositionFromRange(
                    caretRange,
                    out var x,
                    out var y))
            {
                _latestPosition =
                    new CaretPosition(x, y);

                return;
            }

            _latestPosition = null;
        }
        catch
        {
            _latestPosition = null;
        }
    }

    private static bool TryGetPositionFromRange(
        TextPatternRange range,
        out int x,
        out int y)
    {
        var rectangles =
            range.GetBoundingRectangles();

        if (rectangles.Length > 0)
        {
            var rect = rectangles[^1];

            x = (int)Math.Round(rect.Right);
            y = (int)Math.Round(rect.Bottom);

            return true;
        }

        // У обычного caret диапазон часто имеет длину 0.
        // Тогда UI Automation возвращает пустой массив rect.
        // Расширяем копию до одного символа.
        var expanded = range.Clone();

        try
        {
            expanded.ExpandToEnclosingUnit(
                TextUnit.Character
            );

            rectangles =
                expanded.GetBoundingRectangles();

            if (rectangles.Length == 0)
            {
                x = 0;
                y = 0;
                return false;
            }

            var rect = rectangles[0];

            var atStart =
                range.CompareEndpoints(
                    TextPatternRangeEndpoint.Start,
                    expanded,
                    TextPatternRangeEndpoint.Start
                ) == 0;

            x = (int)Math.Round(
                atStart
                    ? rect.Left
                    : rect.Right
            );

            y = (int)Math.Round(rect.Bottom);

            return true;
        }
        catch
        {
            x = 0;
            y = 0;
            return false;
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }

    private sealed record CaretPosition(
        int X,
        int Y
    );
}