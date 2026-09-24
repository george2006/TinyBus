using System;
using System.Text;

namespace TinyBus.SourceGen.Generation.Emission;

internal sealed class SourceWriter
{
    private readonly StringBuilder builder = new StringBuilder();
    private int indent;

    public void WriteLine(string text = "")
    {
        if (text.Length > 0)
        {
            var spaces = indent * 4;
            builder.Append(' ', spaces);
        }

        builder.AppendLine(text);
    }

    public void Indent()
    {
        indent++;
    }

    public void Unindent()
    {
        if (indent == 0)
        {
            throw new InvalidOperationException("Source writer indentation cannot be negative.");
        }

        indent--;
    }

    public override string ToString()
    {
        return builder.ToString();
    }
}
