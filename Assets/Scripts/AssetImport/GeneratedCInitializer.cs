using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Blitz.AssetImport
{
    // Only generated data declarations are accepted: no preprocessor, calls, casts, or executable C.
    internal sealed class GeneratedCInitializer
    {
        internal sealed class Value
        {
            internal SourceLocation Location;
            internal string Text;
            internal double Number;
            internal bool IsInteger;
            internal string Kind;
            internal List<Value> Items;
        }

        internal sealed class Declaration
        {
            internal string Type;
            internal string Name;
            internal bool Pointer;
            internal bool Array;
            internal Value Initializer;
            internal SourceLocation Location;
        }

        private sealed class Token
        {
            internal string Kind;
            internal string Text;
            internal SourceLocation Location;
        }

        private readonly string text;
        private readonly string file;
        private int offset;
        private int line = 1;
        private int column = 1;
        private int tokenCount;
        private int valueCount;
        private Token current;

        private GeneratedCInitializer(string source, string label)
        {
            if (source == null || source.Length > PlayerImportLimits.SourceBytes ||
                Encoding.UTF8.GetByteCount(source) > PlayerImportLimits.SourceBytes)
                throw new PlayerAssetException("Generated C source exceeds the 8 MiB limit or is null.");
            text = source;
            file = label;
            if (text.Length > 0 && text[0] == '\ufeff') Advance();
            Next();
        }

        internal static Dictionary<string, Declaration> Parse(string source, string label)
        {
            return new GeneratedCInitializer(source, label).Document();
        }

        private Dictionary<string, Declaration> Document()
        {
            var result = new Dictionary<string, Declaration>(StringComparer.Ordinal);
            while (current.Kind != "end")
            {
                SourceLocation location = current.Location;
                var qualifiers = new HashSet<string>(StringComparer.Ordinal);
                while (current.Text == "static" || current.Text == "const")
                {
                    if (!qualifiers.Add(current.Text)) Error("Duplicate declaration qualifier.");
                    Next();
                }
                string type = Identifier();
                if (type != "VERTEX" && type != "ST" && type != "TRI" && type != "LIMB" && type != "SNODE" && type != "char")
                    throw new PlayerAssetException(location, "Unsupported type '" + type + "'; expected generated VERTEX/ST/TRI/LIMB/SNODE or char* data.");
                bool pointer = Take("*");
                string name = Identifier();
                bool array = Take("[");
                int dimension = -1;
                if (array)
                {
                    if (current.Text != "]")
                    {
                        Value count = Numeric();
                        dimension = Integer(count);
                        if (dimension < 1 || dimension > PlayerImportLimits.ArrayElements)
                            throw new PlayerAssetException(count.Location, "Array dimension must be 1..262144.");
                    }
                    Expect("]");
                }
                if (pointer ? !array || (type != "LIMB" && type != "char") : type == "char" || (type != "LIMB" && !array))
                    throw new PlayerAssetException(location, "Unsupported declaration shape for '" + name + "'.");
                Expect("=");
                Value initializer = List(0);
                Expect(";");
                if (dimension >= 0 && dimension != initializer.Items.Count)
                    throw new PlayerAssetException(location, "Declared dimension for '" + name + "' is " + dimension + " but initializer has " + initializer.Items.Count + " entries (including NULL).");
                var declaration = new Declaration { Type = type, Name = name, Pointer = pointer,
                    Array = array, Initializer = initializer, Location = location };
                if (result.ContainsKey(name)) throw new PlayerAssetException(location, "Duplicate symbol '" + name + "'.");
                if (result.Count >= PlayerImportLimits.Declarations)
                    throw new PlayerAssetException(location, "Too many declarations (limit 4096).");
                result.Add(name, declaration);
            }
            return result;
        }

        private Value List(int depth)
        {
            if (depth > 3) Error("Initializer nesting exceeds the accepted generated grammar.");
            SourceLocation location = current.Location;
            Expect("{");
            var items = new List<Value>();
            while (current.Text != "}")
            {
                if (current.Kind == "end") Error("Unterminated initializer; expected '}'.");
                if (items.Count >= PlayerImportLimits.ArrayElements || ++valueCount > PlayerImportLimits.Tokens)
                    Error("Initializer exceeds the element limit.");
                Value item;
                if (current.Text == "{") item = List(depth + 1);
                else if (current.Kind == "number" || current.Text == "+" || current.Text == "-") item = Numeric();
                else if (current.Kind == "string")
                {
                    item = new Value { Kind = "string", Text = current.Text, Location = current.Location };
                    Next();
                }
                else
                {
                    bool address = Take("&");
                    SourceLocation itemLocation = current.Location;
                    string symbol = Identifier();
                    item = new Value { Kind = address ? "address" : "symbol", Text = symbol, Location = itemLocation };
                }
                items.Add(item);
                if (!Take(",")) break;
            }
            Expect("}");
            return new Value { Kind = "list", Items = items, Location = location };
        }

        private Value Numeric()
        {
            SourceLocation location = current.Location;
            int sign = Take("-") ? -1 : 1;
            if (sign == 1) Take("+");
            if (current.Kind != "number") Error("Expected a finite numeric literal, not an expression or function.");
            string literal = current.Text;
            bool integer = literal.IndexOfAny(new[] { '.', 'e', 'E', 'f', 'F' }) < 0;
            string number = literal.TrimEnd('f', 'F');
            double value;
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.IsInfinity(value) || double.IsNaN(value))
                Error("Numeric literal must be finite.");
            value *= sign;
            Next();
            int operations = 0;
            while (current.Text == "-" || current.Text == "+")
            {
                if (!integer || ++operations > 8) Error("Only bounded integer addition/subtraction is accepted (for self-relative child offsets).");
                int operandSign = current.Text == "-" ? -1 : 1;
                Next();
                int operand = 0;
                if (current.Kind != "number" || !int.TryParse(current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out operand))
                    Error("Expected a decimal integer after child-offset '+'/'-'.");
                value += operandSign * (double)operand;
                if (value < int.MinValue || value > int.MaxValue) Error("Integer expression overflows Int32.");
                Next();
            }
            return new Value { Kind = "number", Number = value, IsInteger = integer, Location = location };
        }

        internal static int Integer(Value value)
        {
            if (value.Kind != "number" || !value.IsInteger || value.Number < int.MinValue || value.Number > int.MaxValue)
                throw new PlayerAssetException(value.Location, "Expected an Int32 decimal integer.");
            return (int)value.Number;
        }

        internal static float FiniteFloat(Value value)
        {
            if (value.Kind != "number" || Math.Abs(value.Number) > PlayerImportLimits.CoordinateMagnitude)
                throw new PlayerAssetException(value.Location, "Expected a finite numeric coordinate with magnitude <= 1000000.");
            float result = (float)value.Number;
            if (float.IsNaN(result) || float.IsInfinity(result))
                throw new PlayerAssetException(value.Location, "Coordinate is not finite.");
            return result;
        }

        private string Identifier()
        {
            if (current.Kind != "identifier") Error("Expected a generated-data identifier.");
            string name = current.Text;
            Next();
            return name;
        }
        private bool Take(string token)
        {
            if (current.Text != token) return false;
            Next(); return true;
        }
        private void Expect(string token)
        {
            if (!Take(token)) Error("Expected '" + token + "', got '" + current.Text + "'.");
        }
        private void Error(string message) { throw new PlayerAssetException(current.Location, message); }
        private char Peek(int distance = 0) { return offset + distance < text.Length ? text[offset + distance] : '\0'; }
        private void Advance()
        {
            if (text[offset++] == '\n') { line++; column = 1; }
            else column++;
        }
        private void Next()
        {
            SkipTrivia();
            var location = new SourceLocation(file, line, column);
            if (++tokenCount > PlayerImportLimits.Tokens)
                throw new PlayerAssetException(location, "Token limit exceeded.");
            if (offset == text.Length) { current = new Token { Kind = "end", Text = "<EOF>", Location = location }; return; }
            int start = offset;
            char c = Peek();
            if (IsLetter(c) || c == '_')
            {
                Advance();
                while (IsLetter(Peek()) || IsDigit(Peek()) || Peek() == '_') Advance();
                if (offset - start > 128) throw new PlayerAssetException(location, "Identifier exceeds 128 characters.");
                current = new Token { Kind = "identifier", Text = text.Substring(start, offset - start), Location = location };
            }
            else if (IsDigit(c) || (c == '.' && IsDigit(Peek(1))))
            {
                while (IsDigit(Peek())) Advance();
                if (Peek() == '.') { Advance(); while (IsDigit(Peek())) Advance(); }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    Advance();
                    if (Peek() == '+' || Peek() == '-') Advance();
                    if (!IsDigit(Peek())) throw new PlayerAssetException(location, "Malformed numeric exponent.");
                    while (IsDigit(Peek())) Advance();
                }
                if (Peek() == 'f' || Peek() == 'F') Advance();
                if (offset - start > 64) throw new PlayerAssetException(location, "Numeric literal exceeds 64 characters.");
                current = new Token { Kind = "number", Text = text.Substring(start, offset - start), Location = location };
            }
            else if (c == '"')
            {
                Advance();
                var value = new StringBuilder();
                while (Peek() != '"')
                {
                    c = Peek();
                    if (c < 32 || c > 126) throw new PlayerAssetException(location, "Unterminated or non-ASCII material string.");
                    if (c == '\\')
                    {
                        Advance(); c = Peek();
                        if (c != '\\' && c != '"') throw new PlayerAssetException(location, "Only escaped quote/backslash is accepted in strings.");
                    }
                    value.Append(c); Advance();
                    if (value.Length > 256) throw new PlayerAssetException(location, "Material string exceeds 256 characters.");
                }
                Advance();
                current = new Token { Kind = "string", Text = value.ToString(), Location = location };
            }
            else if ("{}[]*&,;=+-".IndexOf(c) >= 0)
            {
                Advance();
                current = new Token { Kind = "punctuation", Text = c.ToString(), Location = location };
            }
            else throw new PlayerAssetException(location, "Unsupported character '" + c + "'; arbitrary C/preprocessor input is not accepted.");
        }

        private void SkipTrivia()
        {
            while (offset < text.Length)
            {
                if (char.IsWhiteSpace(Peek())) { Advance(); continue; }
                if (Peek() == '/' && Peek(1) == '/')
                {
                    while (offset < text.Length && Peek() != '\n') Advance();
                    continue;
                }
                if (Peek() == '/' && Peek(1) == '*')
                {
                    var location = new SourceLocation(file, line, column);
                    Advance(); Advance();
                    while (offset < text.Length && !(Peek() == '*' && Peek(1) == '/')) Advance();
                    if (offset == text.Length) throw new PlayerAssetException(location, "Unterminated block comment.");
                    Advance(); Advance(); continue;
                }
                break;
            }
        }
        private static bool IsLetter(char c) { return c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z'; }
        private static bool IsDigit(char c) { return c >= '0' && c <= '9'; }
    }
}
