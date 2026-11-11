using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FileFlow.Core;
using FileFlow.Core.Formatting;

namespace FileFlow.App.Converters;

/// <summary>long bytes -> "2.4 MB".</summary>
public sealed class BytesToHumanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long bytes ? ByteSizeFormatter.Format(bytes) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>UTC DateTime -> local time string.</summary>
public sealed class UtcToLocalConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc || utc == default)
        {
            return string.Empty;
        }

        var format = parameter as string ?? "yyyy-MM-dd HH:mm";
        return utc.ToLocalTime().ToString(format, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>null/empty -> Collapsed, otherwise Visible. Parameter "invert" flips the result.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Any non-null object counts as a value: an earlier version applied string
        // semantics to every input, which hid the details panel for a selected object.
        var visible = value is not null && !(value is string text && text.Length == 0);
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>0 -> Visible (empty state), otherwise Collapsed. Parameter "invert" flips the result.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visibleWhenZero = value is int count && count == 0;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            visibleWhenZero = !visibleWhenZero;
        }

        return visibleWhenZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>TaskState -> colored brush for the status pill text.</summary>
public sealed class TaskStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            TaskState.Queued => "StatusQueuedText",
            TaskState.Running => "StatusRunningText",
            TaskState.Completed => "StatusCompletedText",
            TaskState.CompletedWithErrors => "StatusPartialText",
            TaskState.Failed => "StatusFailedText",
            TaskState.Cancelled => "StatusCancelledText",
            TaskState.Interrupted => "StatusInterruptedText",
            _ => "StatusCancelledText",
        };
        return System.Windows.Application.Current.TryFindResource(key) as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>TaskState -> background brush for the status pill.</summary>
public sealed class TaskStateToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            TaskState.Queued => "StatusQueuedBackground",
            TaskState.Running => "StatusRunningBackground",
            TaskState.Completed => "StatusCompletedBackground",
            TaskState.CompletedWithErrors => "StatusPartialBackground",
            TaskState.Failed => "StatusFailedBackground",
            TaskState.Cancelled => "StatusCancelledBackground",
            TaskState.Interrupted => "StatusInterruptedBackground",
            _ => "StatusCancelledBackground",
        };
        return App.Current.TryFindResource(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.LightGray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool + ConverterParameter ("true"/"false") -> Visibility.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Enum value -> its ToString, used as fallback display.</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
