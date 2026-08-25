using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace CapsCaret;

internal sealed class AutomationCaretProvider :
    IDisposable
{
    private readonly BlockingCollection<Action>
        _queue = new();

    private readonly Thread _thread;

    public event Action<long, int, int>?
        PositionUpdated;

    public AutomationCaretProvider()
    {
        _thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "CapsCaret UI Automation"
        };

        _thread.SetApartmentState(
            ApartmentState.MTA
        );

        _thread.Start();
    }

    public void RequestUpdate(
        long requestVersion)
    {
        if (_queue.IsAddingCompleted)
            return;

        try
        {
            _queue.Add(
                () =>
                    UpdateCaretPosition(
                        requestVersion
                    )
            );
        }
        catch (InvalidOperationException)
        {
            // Application is shutting down.
        }
    }

    private void WorkerLoop()
    {
        foreach (
            var action
            in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch
            {
                // Another application's UIA provider
                // must never crash CapsCaret.
            }
        }
    }

    private void UpdateCaretPosition(
        long requestVersion)
    {
        try
        {
            var element =
                AutomationElement.FocusedElement;

            if (element is null)
                return;

            if (!element.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out var patternObject))
            {
                return;
            }

            var pattern =
                (TextPattern)patternObject;

            var selections =
                pattern.GetSelection();

            if (selections.Length == 0)
                return;

            var caretRange =
                selections[0];

            if (!TryGetPositionFromRange(
                    caretRange,
                    out var x,
                    out var y))
            {
                return;
            }

            PositionUpdated?.Invoke(
                requestVersion,
                x,
                y
            );
        }
        catch
        {
            // Broken or temporarily unavailable
            // UI Automation provider.
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
            var rect =
                rectangles[^1];

            x = (int)Math.Round(
                rect.Right
            );

            y = (int)Math.Round(
                rect.Bottom
            );

            return true;
        }

        // A caret is often a zero-length text range.
        // Such a range can have no bounding rectangle,
        // so expand a copy to one character.
        var expanded =
            range.Clone();

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

            var rect =
                rectangles[0];

            bool atStart =
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

            y = (int)Math.Round(
                rect.Bottom
            );

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
        if (!_queue.IsAddingCompleted)
        {
            _queue.CompleteAdding();
        }

        // Give worker a moment to finish cleanly.
        _thread.Join(300);
    }
}