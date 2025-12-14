using Continuance.Models;

namespace Continuance.CLI
{
    public static class PresetThemes
    {
        public static readonly Dictionary<string, ColorPalette> Themes = new()
        {
            { "Default (Continuance)", new ColorPalette() },
            { "Vaporwave", new ColorPalette {
                Success = "lime",
                Error = "fuchsia",
                Warning = "yellow",
                Info = "aqua",
                Prompt = "magenta",
                Header = "fuchsia",
                Muted = "grey",
                Accent1 = "blue"
            }},
            { "Forest", new ColorPalette {
                Success = "green",
                Error = "maroon",
                Warning = "olive",
                Info = "darkgreen",
                Prompt = "yellow",
                Header = "lime",
                Muted = "grey",
                Accent1 = "green"
            }},
            { "Ocean", new ColorPalette {
                Success = "teal",
                Error = "red",
                Warning = "yellow",
                Info = "blue",
                Prompt = "cyan",
                Header = "navy",
                Muted = "grey",
                Accent1 = "aqua"
            }},
            { "Matrix", new ColorPalette {
                Success = "green",
                Error = "green",
                Warning = "green",
                Info = "green",
                Prompt = "green",
                Header = "lime",
                Muted = "grey",
                Accent1 = "lime"
            }},
            { "Solarized Dark", new ColorPalette {
                Success = "green",
                Error = "red",
                Warning = "yellow",
                Info = "blue",
                Prompt = "cyan",
                Header = "magenta",
                Muted = "grey",
                Accent1 = "olive"
            }},
            { "Dracula", new ColorPalette {
                Success = "lime",
                Error = "magenta",
                Warning = "yellow",
                Info = "cyan",
                Prompt = "purple",
                Header = "fuchsia",
                Muted = "grey",
                Accent1 = "aqua"
            }},
            { "Nord", new ColorPalette {
                Success = "teal",
                Error = "maroon",
                Warning = "yellow",
                Info = "blue",
                Prompt = "navy",
                Header = "purple",
                Muted = "grey",
                Accent1 = "silver"
            }},
        };
    }

    public static class Theme
    {
        public static ColorPalette Current { get; private set; } = new ColorPalette();

        public static void Load(ColorPalette? palette)
        {
            Current = palette ?? new ColorPalette();
        }
    }
}