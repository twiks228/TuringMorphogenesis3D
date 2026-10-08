using System.ComponentModel;

namespace TuringMorphogenesis3D.Localization;

/// <summary>
/// Элемент списка со значением и локализованной подписью.
/// Подпись обновляется автоматически при смене языка.
/// </summary>
public sealed class LocalizedItem<T> : INotifyPropertyChanged
{
    private readonly string _key;

    public T Value { get; }
    public string Label => LocalizationManager.Instance[_key];

    public LocalizedItem(T value, string key)
    {
        Value = value;
        _key = key;
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}