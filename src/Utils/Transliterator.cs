using System.Text;
using System.Text.RegularExpressions;

namespace ScraperAcesso.Utils
{
    public static partial class Transliterator
    {
        // Используем частичный класс и сгенерированный Regex для производительности
        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpaceRegex();

        [GeneratedRegex(@"[^\w\-\.]")]
        private static partial Regex UnsafeCharsRegex();
        
        private static readonly Dictionary<char, string> TranslitMap = new()
        {
            {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"}, {'е', "e"}, {'ё', "yo"},
            {'ж', "zh"}, {'з', "z"}, {'и', "i"}, {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"},
            {'н', "n"}, {'о', "o"}, {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"}, {'у', "u"},
            {'ф', "f"}, {'х', "h"}, {'ц', "c"}, {'ч', "ch"}, {'ш', "sh"}, {'щ', "sch"},
            {'ъ', ""}, {'ы', "y"}, {'ь', ""}, {'э', "e"}, {'ю', "yu"}, {'я', "ya"},
            // Дополнительные символы, если нужно
            {' ', "-"}, {'&', "and"}
        };

        /// <summary>
        /// Converts a Cyrillic string to a URL- and filesystem-safe ASCII string.
        /// </summary>
        /// <param name="cyrillicText">The text to transliterate.</param>
        /// <returns>A safe ASCII string.</returns>
        public static string ToSafeId(string cyrillicText)
        {
            if (string.IsNullOrWhiteSpace(cyrillicText))
            {
                return string.Empty;
            }

            var lowerText = cyrillicText.ToLowerInvariant();
            var sb = new StringBuilder(lowerText.Length);

            foreach (var ch in lowerText)
            {
                if (TranslitMap.TryGetValue(ch, out var translitChar))
                {
                    sb.Append(translitChar);
                }
                // Проверяем, является ли символ буквой или цифрой ASCII
                else if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '.')
                {
                    sb.Append(ch);
                }
            }

            // Заменяем множественные пробелы/тире на одно тире
            string result = WhiteSpaceRegex().Replace(sb.ToString(), "-");

            // Удаляем все небезопасные символы
            result = UnsafeCharsRegex().Replace(result, "");

            // Убираем возможные тире в начале и в конце
            return result.Trim('-');
        }
    }
}