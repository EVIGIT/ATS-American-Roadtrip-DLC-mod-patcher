namespace ATSRoadTripConverter;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The pages that overlay the converter view, so the last one can be remembered (roadmap item 7).
/// <para>
/// Deliberately its own dependency-free file, like <see cref="SettingsSchema"/> and
/// <see cref="MainLayout"/>, rather than nested in <see cref="ConverterForm"/>. It is persisted
/// in the settings file, and nesting it in the Form would make <c>Settings.cs</c> depend on the
/// entire WinForms interface — the exact coupling the other standalone types exist to avoid.
/// </para>
/// <para>
/// A plain enum rather than a persisted string: an unrecognised value in an old settings file then
/// deserialises to <see cref="Converter"/>, which is the main view and therefore the safe
/// fallback, instead of matching nothing.
/// </para>
/// </summary>
public enum MainPage
{
    /// <summary>The converter itself. Nothing to restore.</summary>
    Converter = 0,

    /// <summary>The Settings page.</summary>
    Settings = 1,

    /// <summary>The Changelog page.</summary>
    Changelog = 2
}

/// <summary>
/// Reads a <see cref="MainPage"/> without ever throwing, falling back to
/// <see cref="MainPage.Converter"/> for anything unrecognised.
/// <para>
/// The reason this exists is a measured one, and it is not the obvious one.
/// <see cref="JsonSerializer"/> does <em>not</em> validate enum numbers: deserialising
/// <c>"LastPage": 99</c> succeeds and yields an undefined <see cref="MainPage"/> value, which
/// <c>SettingsManager.Load</c> would happily store. A JSON <em>string</em> in that field, by
/// contrast, throws a <see cref="JsonException"/> — and because the settings file is deserialised
/// as a single object, that exception discards the entire file, resetting the user's theme,
/// accent colour, vehicle types and every other preference.
/// </para>
/// <para>
/// So the converter's job is twofold: turn a throw into a fallback, and reject a number that is
/// not a defined member. Losing one remembered page is a non-event; losing every preference
/// because of a stray string is not.
/// </para>
/// </summary>
public sealed class MainPageJsonConverter : JsonConverter<MainPage>
{
    public override MainPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // A number is what this converter writes. Validate it rather than casting blindly:
        // Enum.ToObject happily produces a value that is not a defined member.
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
            return Enum.IsDefined(typeof(MainPage), numeric) ? (MainPage)numeric : MainPage.Converter;

        // Tolerate the member name too, so a hand-edited file saying "Settings" works.
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            return Enum.TryParse<MainPage>(text, ignoreCase: true, out var parsed) &&
                   Enum.IsDefined(typeof(MainPage), parsed)
                ? parsed
                : MainPage.Converter;
        }

        // Null, true, an object or an array: nothing sensible to restore, so ignore it.
        reader.Skip();
        return MainPage.Converter;
    }

    public override void Write(Utf8JsonWriter writer, MainPage value, JsonSerializerOptions options)
        => writer.WriteNumberValue((int)value);
}