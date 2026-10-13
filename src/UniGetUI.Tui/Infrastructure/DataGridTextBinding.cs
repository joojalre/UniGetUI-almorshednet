using System.Linq.Expressions;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace UniGetUI.Tui.Infrastructure;

/// <summary>Displays already typed strings without the DataGrid's reflection-based default converter.</summary>
internal static class DataGridTextBinding
{
    private static readonly IValueConverter TextConverter = new FuncValueConverter<string, string>(value => value ?? string.Empty);

    public static CompiledBinding Create<T>(Expression<Func<T, string>> property)
    {
        var binding = CompiledBinding.Create(property);
        binding.Mode = BindingMode.OneWay;
        binding.Converter = TextConverter;
        return binding;
    }
}
