"""Bounded C data reader, not a C interpreter or compiler."""

import math
import re
import struct
from dataclasses import dataclass


class SourceError(ValueError):
    pass


def f32(value):
    try:
        result = struct.unpack("<f", struct.pack("<f", value))[0]
    except (OverflowError, struct.error) as exc:
        raise SourceError("C float overflow") from exc
    if not math.isfinite(result):
        raise SourceError("Nonfinite C float")
    return result


def without_comments(text):
    result = list(text)
    i = 0
    while i < len(text):
        if text[i] in "\"'":
            quote = text[i]
            i += 1
            while i < len(text) and text[i] != quote:
                if text[i] == "\\":
                    i += 1
                i += 1
            if i >= len(text):
                raise SourceError("Unterminated C string/character literal")
            i += 1
        elif text.startswith("//", i) or text.startswith("/*", i):
            if text.startswith("//", i):
                end = text.find("\n", i)
                end = len(text) if end < 0 else end
            else:
                end = text.find("*/", i + 2)
                if end < 0:
                    raise SourceError("Unterminated C comment")
                end += 2
            for j in range(i, end):
                if text[j] != "\n":
                    result[j] = " "
            i = end
        else:
            i += 1
    return "".join(result)


@dataclass(frozen=True)
class Macro:
    text: str
    parameters: object = None


_TOKEN = re.compile(
    r"\s*(0[xX][0-9a-fA-F]+[uUlL]*|"
    r"(?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?[fFuUlL]*|"
    r"[A-Za-z_]\w*|<<|>>|<=|>=|==|!=|&&|\|\||[()+\-*/%~!&|^<>,])"
)
_PREC = {"||": 1, "&&": 2, "|": 3, "^": 4, "&": 5,
         "==": 6, "!=": 6, "<": 7, ">": 7, "<=": 7, ">=": 7,
         "<<": 8, ">>": 8, "+": 9, "-": 9, "*": 10, "/": 10, "%": 10}


class Expressions:
    def __init__(self, macros=None):
        self.macros = {} if macros is None else dict(macros)
        self.history = []

    def at_line(self, line):
        macros = dict(self.macros)
        for at, name, previous in reversed(self.history):
            if at > line:
                if previous is None:
                    macros.pop(name, None)
                else:
                    macros[name] = previous
        return Expressions(macros)

    def evaluate(self, text, unknown_zero=False, stack=(), bindings=None):
        if len(stack) > 32 or len(text) > 4096:
            raise SourceError("C expression expansion limit")
        tokens, pos = [], 0
        while pos < len(text.rstrip()):
            match = _TOKEN.match(text, pos)
            if not match:
                raise SourceError("Unsupported C expression syntax: " + text[pos:pos + 48])
            tokens.append(match.group(1))
            pos = match.end()
        if len(tokens) > 256:
            raise SourceError("C expression token limit")
        cursor = 0
        bindings = {} if bindings is None else bindings

        def take():
            nonlocal cursor
            if cursor == len(tokens):
                raise SourceError("Incomplete C expression: " + text)
            value = tokens[cursor]
            cursor += 1
            return value

        def expect(expected):
            if take() != expected:
                raise SourceError("Expected '" + expected + "' in C expression")

        def atom():
            token = take()
            if token in ("+", "-", "~", "!"):
                value = atom()
                if token == "+":
                    return value
                if token == "-":
                    return f32(-value) if isinstance(value, float) else -value
                if token == "!":
                    return int(not value)
                if not isinstance(value, int):
                    raise SourceError("Bitwise C operator requires an integer")
                return ~value
            if token == "(":
                value = expr(1)
                expect(")")
                return value
            if token == "defined":
                parens = cursor < len(tokens) and tokens[cursor] == "("
                if parens:
                    take()
                name = take()
                if not re.fullmatch(r"[A-Za-z_]\w*", name):
                    raise SourceError("Invalid defined() identifier")
                if parens:
                    expect(")")
                return int(name in self.macros)
            if re.fullmatch(r"[A-Za-z_]\w*", token):
                if token in bindings:
                    return bindings[token]
                macro = self.macros.get(token)
                if macro is None:
                    if unknown_zero:
                        return 0
                    raise SourceError("Undefined C constant: " + token)
                if token in stack:
                    raise SourceError("C macro cycle: " + token)
                arguments = {}
                if macro.parameters is not None:
                    expect("(")
                    for i, parameter in enumerate(macro.parameters):
                        if i:
                            expect(",")
                        arguments[parameter] = expr(1)
                    expect(")")
                return self.evaluate(macro.text, unknown_zero, stack + (token,), arguments)
            numeric = re.sub(r"[uUlL]+$", "", token)
            if re.fullmatch(r"0[xX][0-9a-fA-F]+", numeric):
                return int(numeric, 16)
            if "." in numeric or "e" in numeric.lower() or numeric.endswith(("f", "F")):
                try:
                    return f32(float(numeric.rstrip("fF")))
                except ValueError as exc:
                    raise SourceError("Invalid C float: " + token) from exc
            try:
                return int(numeric, 8 if numeric.startswith("0") and len(numeric) > 1 else 10)
            except ValueError as exc:
                raise SourceError("Invalid C integer: " + token) from exc

        def binary(op, left, right):
            if op in ("&&", "||"):
                return int(bool(left) and bool(right)) if op == "&&" else int(bool(left) or bool(right))
            if op in ("==", "!=", "<", ">", "<=", ">="):
                return int({"==": left == right, "!=": left != right, "<": left < right,
                            ">": left > right, "<=": left <= right, ">=": left >= right}[op])
            if op in ("&", "|", "^", "<<", ">>", "%"):
                if not isinstance(left, int) or not isinstance(right, int):
                    raise SourceError("Integer C operator applied to float")
                if op in ("<<", ">>") and not 0 <= right <= 63:
                    raise SourceError("Unsupported C shift count")
                if op == "&":
                    return left & right
                if op == "|":
                    return left | right
                if op == "^":
                    return left ^ right
                if op == "<<":
                    return left << right
                if op == ">>":
                    return left >> right
                if not right:
                    raise SourceError("C division by zero")
                return left - trunc_div(left, right) * right
            if op == "/" and not right:
                raise SourceError("C division by zero")
            if op == "+":
                value = left + right
            elif op == "-":
                value = left - right
            elif op == "*":
                value = left * right
            else:
                value = left / right if isinstance(left, float) or isinstance(right, float) else trunc_div(left, right)
            if isinstance(left, float) or isinstance(right, float):
                return f32(value)
            if abs(value) > 2 ** 64:
                raise SourceError("C integer expression limit")
            return value

        def expr(minimum):
            left = atom()
            while cursor < len(tokens) and _PREC.get(tokens[cursor], 0) >= minimum:
                op = take()
                right = expr(_PREC[op] + 1)
                left = binary(op, left, right)
            return left

        if not tokens:
            raise SourceError("Empty C expression")
        result = expr(1)
        if cursor != len(tokens):
            raise SourceError("Unsupported trailing C expression: " + " ".join(tokens[cursor:]))
        return result


def trunc_div(left, right):
    result = abs(left) // abs(right)
    return -result if (left < 0) != (right < 0) else result


def preprocess(text, environment=None):
    """Preserve physical lines; includes are metadata, never followed/executed."""
    clean = without_comments(text)
    expressions = Expressions(environment)
    result, stack = [], []
    active = True
    for line_number, line in enumerate(clean.splitlines(keepends=True), 1):
        stripped = line.lstrip()
        if not stripped.startswith("#"):
            result.append(line if active else "\n" if line.endswith("\n") else "")
            continue
        match = re.fullmatch(r"#\s*(\w+)(.*)", stripped.rstrip("\r\n"))
        if match is None:
            raise SourceError("Unsupported C preprocessor directive")
        command, value = match.group(1), match.group(2).strip()
        if command in ("if", "ifdef", "ifndef"):
            condition = False
            if active:
                if command == "if":
                    condition = bool(expressions.evaluate(value, unknown_zero=True))
                else:
                    condition = value in expressions.macros
                    if command == "ifndef":
                        condition = not condition
            stack.append([active, condition, False])
            active = active and condition
        elif command in ("elif", "else"):
            if not stack or stack[-1][2]:
                raise SourceError("Misordered C #else/#elif")
            parent, used, _ = stack[-1]
            condition = parent and not used
            if command == "elif" and condition:
                condition = bool(expressions.evaluate(value, unknown_zero=True))
            stack[-1][1] = used or condition
            stack[-1][2] = command == "else"
            active = parent and condition
        elif command == "endif":
            if not stack:
                raise SourceError("Unmatched C #endif")
            active = stack.pop()[0]
        elif active and command == "define":
            macro = re.fullmatch(r"([A-Za-z_]\w*)(\(([^()]*)\))?(?:\s+(.*))?", value)
            if macro is None:
                raise SourceError("Unsupported multiline or complex C macro: " + value[:64])
            parameters = None if macro.group(2) is None else tuple(p.strip() for p in macro.group(3).split(",") if p.strip())
            expressions.history.append((line_number, macro.group(1), expressions.macros.get(macro.group(1))))
            expressions.macros[macro.group(1)] = Macro(macro.group(4) or "", parameters)
        elif active and command == "undef":
            expressions.history.append((line_number, value, expressions.macros.get(value)))
            expressions.macros.pop(value, None)
        elif active and command == "error":
            raise SourceError("Active source #error: " + value)
        elif command not in ("include", "define", "undef", "pragma", "error"):
            raise SourceError("Unsupported C directive: " + command)
        result.append("\n" if line.endswith("\n") else "")
    if stack:
        raise SourceError("Unclosed C preprocessor conditional")
    return "".join(result), expressions


@dataclass(frozen=True)
class Scalar:
    raw: str
    line: int


def split_values(text, delimiter=","):
    values, start, depth, quote, escaped = [], 0, 0, None, False
    for i, char in enumerate(text):
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char in "({[":
            depth += 1
        elif char in ")}]":
            depth -= 1
        elif char == delimiter and not depth:
            values.append((text[start:i], start))
            start = i + 1
    if quote or depth:
        raise SourceError("Unbalanced C data expression")
    if text[start:].strip():
        values.append((text[start:], start))
    return values


def closing_brace(text, start):
    depth, quote, escaped = 0, None, False
    for i in range(start, len(text)):
        char = text[i]
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if not depth:
                return i
    raise SourceError("Unclosed C initializer/function")


class CData:
    def __init__(self, text, environment=None):
        self.text, self.expressions = preprocess(text, environment)

    def enums(self):
        for match in re.finditer(r"\benum(?:\s+[A-Za-z_]\w*)?\s*\{([^{}]*)\}", self.text):
            number = 0
            for raw, _ in split_values(match.group(1)):
                entry = re.fullmatch(r"\s*([A-Za-z_]\w*)\s*(?:=\s*(.*))?", raw)
                if entry is None:
                    raise SourceError("Unsupported C enum entry: " + raw)
                if entry.group(2) is not None:
                    number = self.expressions.evaluate(entry.group(2))
                if not isinstance(number, int):
                    raise SourceError("Noninteger C enum")
                self.expressions.macros[entry.group(1)] = Macro(str(number))
                number += 1
        return self

    def function(self, name):
        pattern = r"\b" + re.escape(name) + r"\s*\([^;{}]*\)\s*\{"
        matches = list(re.finditer(pattern, self.text))
        if len(matches) != 1:
            raise SourceError("Expected one C function definition: " + name)
        start = matches[0].end() - 1
        end = closing_brace(self.text, start)
        return self.text[:start].count("\n") + 1, self.text[start:end + 1]

    def array(self, name, scope=None):
        first_line, text = (1, self.text) if scope is None else scope
        pattern = r"\b" + re.escape(name) + r"\s*((?:\[[^\]\n]*\]\s*)+)\s*=\s*\{"
        matches = list(re.finditer(pattern, text))
        if len(matches) != 1:
            raise SourceError("Expected one active C array: " + name)
        match = matches[0]
        start = match.end() - 1
        end = closing_brace(text, start)
        base_line = text[:start].count("\n") + first_line

        def parse(body, line, depth=0):
            if depth > 8:
                raise SourceError("C initializer nesting limit")
            output = []
            for part, offset in split_values(body):
                value = part.strip()
                at = line + body[:offset].count("\n") + part[:len(part) - len(part.lstrip())].count("\n")
                if value.startswith("{"):
                    if closing_brace(value, 0) != len(value) - 1:
                        raise SourceError("Unsupported trailing C initializer syntax")
                    output.append(parse(value[1:-1], at, depth + 1))
                else:
                    if not value or value.startswith(".") and not re.match(r"\.\d", value):
                        raise SourceError("Unsupported C designator/empty initializer")
                    output.append(Scalar(value, at))
            return output

        dimensions = re.findall(r"\[([^\]]*)\]", match.group(1))
        return parse(text[start + 1:end], base_line), dimensions

    def number(self, value):
        if not isinstance(value, Scalar):
            raise SourceError("Expected C scalar number")
        return self.expressions.at_line(value.line).evaluate(value.raw)

    def string(self, value):
        raw = value.raw.strip()
        stack = set()
        while re.fullmatch(r"[A-Za-z_]\w*", raw):
            if raw in stack or raw not in self.expressions.macros:
                raise SourceError("Unresolved C string macro: " + raw)
            stack.add(raw)
            raw = self.expressions.macros[raw].text
        if len(raw) < 2 or raw[0] not in "\"'" or raw[-1] != raw[0]:
            raise SourceError("Expected C string/character literal: " + raw)
        body, result, i = raw[1:-1], [], 0
        escapes = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\", "\"": "\"", "'": "'", "0": "\0"}
        while i < len(body):
            if body[i] != "\\":
                result.append(body[i])
                i += 1
                continue
            i += 1
            if i == len(body) or body[i] not in escapes:
                raise SourceError("Unsupported C escape sequence")
            result.append(escapes[body[i]])
            i += 1
        return "".join(result)

    @staticmethod
    def reference(value):
        raw = value.raw.strip()
        if raw in ("0", "NULL"):
            return None
        match = re.fullmatch(r"&?\s*([A-Za-z_]\w*)", raw)
        if not match:
            raise SourceError("Unsupported C symbol reference: " + raw)
        return match.group(1)
