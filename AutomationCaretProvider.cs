using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace CapsCaret;

internal enum AutomationCaretResultKind
{
    Success,
    TextControlWithoutCaret,
    Unsupported
}

internal readonly record struct AutomationCaretResult(
    AutomationCaretResultKind Kind,
    int X,
    int Y
)
{
    public static AutomationCaretResult Success(
        int x,
        int y)
    {
        return new AutomationCaretResult(
            AutomationCaretResultKind.Success,
            x,
            y
        );
    }

    public static AutomationCaretResult TextControlWithoutCaret()
    {
        return new AutomationCaretResult(
            AutomationCaretResultKind.TextControlWithoutCaret,
            0,
            0
        );
    }

    public static AutomationCaretResult Unsupported()
    {
        return new AutomationCaretResult(
            AutomationCaretResultKind.Unsupported,
            0,
            0
        );
    }
}

internal sealed class AutomationCaretProvider : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public event Action<long, AutomationCaretResult>?
        ResultUpdated;

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
                () => UpdateCaretPosition(
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
            var action in
            _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch
            {
                // A broken accessibility provider must never
                // bring down CapsCaret.
            }
        }
    }

    private void UpdateCaretPosition(
        long requestVersion)
    {
        AutomationElement? element;

        try
        {
            element =
                AutomationElement.FocusedElement;
        }
        catch
        {
            Publish(
                requestVersion,
                AutomationCaretResult.Unsupported()
            );

            return;
        }

        if (element is null)
        {
            Publish(
                requestVersion,
                AutomationCaretResult.Unsupported()
            );

            return;
        }

        object patternObject;

        try
        {
            if (!element.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out patternObject))
            {
                Publish(
                    requestVersion,
                    AutomationCaretResult.Unsupported()
                );

                return;
            }
        }
        catch
        {
            Publish(
                requestVersion,
                AutomationCaretResult.Unsupported()
            );

            return;
        }

        var pattern =
            (TextPattern)patternObject;

        TextPatternRange[] selections;

        try
        {
            selections =
                pattern.GetSelection();
        }
        catch
        {
            // The element IS a text control, but its provider did not
            // give us a usable caret. Do not fall back to classic Win32;
            // custom apps may expose a misleading native caret.
            Publish(
                requestVersion,
                AutomationCaretResult
                    .TextControlWithoutCaret()
            );

            return;
        }

        if (selections.Length == 0)
        {
            Publish(
                requestVersion,
                AutomationCaretResult
                    .TextControlWithoutCaret()
            );

            return;
        }

        try
        {
            if (TryGetPositionFromRange(
                    element,
                    selections[0],
                    out var x,
                    out var y))
            {
                Publish(
                    requestVersion,
                    AutomationCaretResult.Success(
                        x,
                        y
                    )
                );

                return;
            }
        }
        catch
        {
            // Treat this as a supported text control without a
            // trustworthy caret rather than falling through to Win32.
        }

        Publish(
            requestVersion,
            AutomationCaretResult
                .TextControlWithoutCaret()
        );
    }

    private void Publish(
        long requestVersion,
        AutomationCaretResult result)
    {
        ResultUpdated?.Invoke(
            requestVersion,
            result
        );
    }

    private static bool TryGetPositionFromRange(
        AutomationElement element,
        TextPatternRange range,
        out int x,
        out int y)
    {
        var elementBounds =
            TryGetElementBounds(element);

        var rectangles =
            range.GetBoundingRectangles();

        if (rectangles.Length > 0)
        {
            var rect =
                NormalizeProviderRectangle(
                    rectangles[^1],
                    elementBounds
                );

            x =
                (int)Math.Round(
                    rect.Right
                );

            y =
                (int)Math.Round(
                    rect.Bottom
                );

            return true;
        }

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
                NormalizeProviderRectangle(
                    rectangles[0],
                    elementBounds
                );

            bool atStart =
                range.CompareEndpoints(
                    TextPatternRangeEndpoint.Start,
                    expanded,
                    TextPatternRangeEndpoint.Start
                ) == 0;

            x =
                (int)Math.Round(
                    atStart
                        ? rect.Left
                        : rect.Right
                );

            y =
                (int)Math.Round(
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

    private static System.Windows.Rect TryGetElementBounds(
        AutomationElement element)
    {
        try
        {
            return element.Current.BoundingRectangle;
        }
        catch
        {
            return System.Windows.Rect.Empty;
        }
    }

    private static System.Windows.Rect NormalizeProviderRectangle(
        System.Windows.Rect rect,
        System.Windows.Rect elementBounds)
    {
        if (rect.IsEmpty || elementBounds.IsEmpty)
            return rect;

        // UI Automation text geometry is expected in physical screen
        // coordinates. Some custom providers can expose one axis in
        // element-local coordinates instead. Telegram/Qt can exhibit
        // this as a correct X with a vertically displaced caret.
        //
        // Only compensate when the reported coordinate is outside the
        // focused element in screen space AND still looks plausible as
        // a local coordinate inside that element. Correct providers are
        // therefore left untouched.
        const double tolerance = 2.0;

        bool yOutsideScreenBounds =
            rect.Bottom < elementBounds.Top - tolerance ||
            rect.Top > elementBounds.Bottom + tolerance;

        bool yLooksElementLocal =
            rect.Top >= -tolerance &&
            rect.Bottom <=
                elementBounds.Height + tolerance;

        if (yOutsideScreenBounds &&
            yLooksElementLocal)
        {
            rect.Offset(
                0,
                elementBounds.Top
            );
        }

        return rect;
    }

    public void Dispose()
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.CompleteAdding();
        }

        _thread.Join(300);
    }
}
