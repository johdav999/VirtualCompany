using System.Globalization;

namespace VirtualCompany.Domain.Finance;

public sealed record ReportFormulaAnalysis(bool IsValid, IReadOnlyList<string> References, string? Error);

public static class ReportFormulaEngine
{
    public static ReportFormulaAnalysis Analyze(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return new(false, [], "A formula is required.");
        try
        {
            var parser = new Parser(formula);
            parser.Parse();
            return new(true, parser.References.Order(StringComparer.Ordinal).ToArray(), null);
        }
        catch (FormulaException ex) { return new(false, [], ex.Message); }
    }

    public static decimal Evaluate(string formula, IReadOnlyDictionary<string, decimal> values)
    {
        var parser = new Parser(formula, values);
        return decimal.Round(parser.Parse(), 4, MidpointRounding.ToEven);
    }

    public static IReadOnlyList<IReadOnlyList<string>> FindCycles(IReadOnlyDictionary<string, IReadOnlyList<string>> graph)
    {
        var result = new List<IReadOnlyList<string>>();
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        foreach (var node in graph.Keys.Order(StringComparer.Ordinal)) Visit(node);
        return result;

        void Visit(string node)
        {
            if (visited.Contains(node)) return;
            if (!visiting.Add(node))
            {
                var start = path.FindIndex(x => x == node);
                var cycle = path.Skip(Math.Max(0, start)).Append(node).ToArray();
                if (!result.Any(x => string.Join('|', x) == string.Join('|', cycle))) result.Add(cycle);
                return;
            }
            path.Add(node);
            if (graph.TryGetValue(node, out var references))
                foreach (var reference in references.Where(graph.ContainsKey).Order(StringComparer.Ordinal)) Visit(reference);
            path.RemoveAt(path.Count - 1);
            visiting.Remove(node);
            visited.Add(node);
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, decimal>? _values;
        private int _position;
        public HashSet<string> References { get; } = new(StringComparer.Ordinal);
        public Parser(string text, IReadOnlyDictionary<string, decimal>? values = null) { _text = text; _values = values; }
        public decimal Parse()
        {
            var value = Expression(); Skip();
            if (_position != _text.Length) throw Error("Unexpected token.");
            return value;
        }
        private decimal Expression()
        {
            var value = Term();
            while (true) { Skip(); if (Take('+')) value += Term(); else if (Take('-')) value -= Term(); else return value; }
        }
        private decimal Term()
        {
            var value = Factor();
            while (true)
            {
                Skip();
                if (Take('*')) value *= Factor();
                else if (Take('/')) { var divisor = Factor(); if (divisor == 0) throw Error("Division by zero is not allowed."); value /= divisor; }
                else return value;
            }
        }
        private decimal Factor()
        {
            Skip();
            if (Take('+')) return Factor();
            if (Take('-')) return -Factor();
            if (Take('(')) { var value = Expression(); Skip(); if (!Take(')')) throw Error("A closing parenthesis is required."); return value; }
            if (Peek("SUM")) return Sum();
            if (Take('[')) return Reference();
            return Number();
        }
        private decimal Sum()
        {
            _position += 3; Skip(); if (!Take('(')) throw Error("SUM requires parentheses.");
            decimal result = 0; var count = 0;
            while (true)
            {
                result += Expression(); count++; Skip();
                if (Take(')')) break;
                if (!Take(',')) throw Error("SUM arguments must be separated by commas.");
            }
            if (count == 0) throw Error("SUM requires at least one argument.");
            return result;
        }
        private decimal Reference()
        {
            var start = _position;
            while (_position < _text.Length && _text[_position] != ']') _position++;
            if (_position == _text.Length) throw Error("A line reference is missing its closing bracket.");
            var code = _text[start.._position].Trim().ToUpperInvariant(); _position++;
            if (code.Length == 0 || !code.All(x => char.IsLetterOrDigit(x) || x is '_' or '-' or '.')) throw Error("The line reference is invalid.");
            References.Add(code);
            if (_values is null) return 0;
            return _values.TryGetValue(code, out var value) ? value : throw Error($"Line reference [{code}] has no value.");
        }
        private decimal Number()
        {
            var start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
            if (start == _position || !decimal.TryParse(_text[start.._position], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
                throw Error("Only numbers, line references, SUM, and arithmetic operators are allowed.");
            return value;
        }
        private void Skip() { while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++; }
        private bool Take(char value) { if (_position < _text.Length && _text[_position] == value) { _position++; return true; } return false; }
        private bool Peek(string value) => _position + value.Length <= _text.Length && string.Equals(_text.Substring(_position, value.Length), value, StringComparison.OrdinalIgnoreCase);
        private FormulaException Error(string message) => new($"{message} Position {_position + 1}.");
    }
    private sealed class FormulaException(string message) : Exception(message);
}
