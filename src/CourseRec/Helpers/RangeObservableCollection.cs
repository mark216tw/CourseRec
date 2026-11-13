using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 支援批次取代項目的 <see cref="ObservableCollection{T}"/>，以單次 Reset 通知降低 UI 更新次數。
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        CheckReentrancy();
        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
