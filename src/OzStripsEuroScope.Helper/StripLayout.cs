using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OzStripsEuroScope.Helper
{
    internal sealed class StripElementDefinition
    {
        public string Value { get; set; } = "NONE";

        public int X { get; set; }

        public int Y { get; set; }

        public int W { get; set; }

        public int H { get; set; }

        public int FontSize { get; set; } = 12;
    }

    internal sealed class StripLayout
    {
        public IReadOnlyList<StripElementDefinition> Elements { get; private set; } =
            Array.Empty<StripElementDefinition>();

        public int Width => Elements.Count == 0 ? 420 : Elements.Max(element => element.X + element.W);

        public int Height => Elements.Count == 0 ? 60 : Elements.Max(element => element.Y + element.H);

        public static StripLayout Load()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Strip.xml");
            if (!File.Exists(path))
            {
                return Fallback();
            }

            try
            {
                var document = XDocument.Load(path);
                var elements = document.Root?
                    .Elements("StripElement")
                    .Select(element => new StripElementDefinition
                    {
                        Value = ReadString(element, "Value", "NONE"),
                        X = ReadInt(element, "X"),
                        Y = ReadInt(element, "Y"),
                        W = ReadInt(element, "W", 1),
                        H = ReadInt(element, "H", 1),
                        FontSize = ReadInt(element, "FontSize", 12),
                    })
                    .Where(element => element.W > 0 && element.H > 0)
                    .ToArray();

                if (elements == null || elements.Length == 0)
                {
                    return Fallback();
                }

                return new StripLayout { Elements = elements };
            }
            catch
            {
                return Fallback();
            }
        }

        private static StripLayout Fallback()
        {
            return new StripLayout
            {
                Elements = new[]
                {
                    Element("ACID", 0, 0, 140, 60, 20),
                    Element("TYPE", 0, 0, 44, 14, 8),
                    Element("WTC", 44, 0, 20, 14, 8),
                    Element("ADES", 92, 0, 48, 14, 8),
                    Element("RWY", 140, 0, 45, 20, 10),
                    Element("RFL", 140, 20, 22, 20, 8),
                    Element("CFL", 162, 20, 23, 20, 8),
                    Element("SSR", 140, 40, 45, 20, 9),
                    Element("GLOP", 185, 0, 170, 36, 11),
                    Element("SID", 185, 36, 85, 24, 11),
                    Element("FIRST_WPT", 270, 36, 85, 24, 10),
                    Element("STAND", 355, 0, 35, 60, 10),
                    Element("FRUL", 390, 0, 30, 30, 10),
                    Element("PDC_INDICATOR", 390, 30, 30, 30, 10),
                },
            };
        }

        private static StripElementDefinition Element(string value, int x, int y, int w, int h, int fontSize)
        {
            return new StripElementDefinition
            {
                Value = value,
                X = x,
                Y = y,
                W = w,
                H = h,
                FontSize = fontSize,
            };
        }

        private static string ReadString(XElement element, string name, string defaultValue)
        {
            return element.Element(name)?.Value ?? defaultValue;
        }

        private static int ReadInt(XElement element, string name, int defaultValue = 0)
        {
            return int.TryParse(element.Element(name)?.Value, out var value) ? value : defaultValue;
        }
    }
}
