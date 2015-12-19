using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;

namespace ValveResourceFormat.KeyValues
{
    //Different type of value blocks for KeyValues3
    public enum KVType
    {
        OBJECT,
        ARRAY,
        BOOLEAN,
        INTEGER,
        DOUBLE,
        STRING,
        STRING_MULTI,
        FLAGGED_STRING
    }

    //Datastructure for a KV Object
    public class KVObject
    {
        public string Key;
        public Dictionary<String, KVValue> Properties;
        private bool IsArray;
        public int Count;

        public KVObject(string name)
        {
            Key = name;
            Properties = new Dictionary<String, KVValue>();
            IsArray = false;
            Count = 0;
        }

        public KVObject(string name, bool isArray)
            : this(name)
        {
            IsArray = isArray;
        }

        //Add a property to the structure
        public virtual void AddProperty(String name, KVValue value)
        {
            if (IsArray)
            {
                //Make up a key for the dictionary
                Properties.Add(Count.ToString(), value);
            }
            else
            {
                Properties.Add(name, value);
            }

            Count++;
        }

        public void Serialize(StringBuilder stringBuilder, int indent)
        {
            if (IsArray)
            {
                SerializeArray(stringBuilder, indent);
            }
            else
            {
                SerializeObject(stringBuilder, indent);
            }
        }

        //Serialize the contents of the KV object
        private void SerializeObject(StringBuilder stringBuilder, int indent)
        {
            //Don't enter the top-most object
            if (indent > 1)
            {
                stringBuilder.Append("\n");
            }

            PrintIndent(stringBuilder, indent - 1);
            stringBuilder.Append("{\n");

            foreach (var pair in Properties)
            {
                PrintIndent(stringBuilder, indent);

                stringBuilder.Append(pair.Key);
                stringBuilder.Append(" = ");

                PrintValue(stringBuilder, pair.Value, indent);

                stringBuilder.Append("\n");
            }

            PrintIndent(stringBuilder, indent - 1);
            stringBuilder.Append("}");
        }

        private void SerializeArray(StringBuilder stringBuilder, int indent)
        {
            stringBuilder.Append("\n");
            PrintIndent(stringBuilder, indent - 1);
            stringBuilder.Append("[\n");

            //Need to preserve the order
            for (var i = 0; i < Count; i++)
            {
                PrintIndent(stringBuilder, indent);

                PrintValue(stringBuilder, Properties[i.ToString()], indent);

                stringBuilder.Append(",\n");
            }

            PrintIndent(stringBuilder, indent - 1);
            stringBuilder.AppendLine("]");
        }

        private string EscapeUnescaped(string input, char toEscape)
        {
            int index = 1;
            while (true)
            {
                index = input.IndexOf(toEscape, index);

                //Break out of the loop if no more occurrences were found
                if (index == -1)
                {
                    break;
                }

                if (input.ElementAt(index - 1) != '\\')
                {
                    input = input.Insert(index, "\\");
                }

                //Don't read this one again
                index++;
            }
            return input;
        }

        //Print a value in the correct representation
        private void PrintValue(StringBuilder stringBuilder, KVValue kvValue, int indent)
        {
            KVType type = kvValue.Type;
            object value = kvValue.Value;

            switch (type)
            {
                case KVType.OBJECT:
                    ((KVObject)value).Serialize(stringBuilder, indent + 1);
                    break;
                case KVType.ARRAY:
                    ((KVObject)value).Serialize(stringBuilder, indent + 1);
                    break;
                case KVType.FLAGGED_STRING:
                    stringBuilder.Append((string)value);
                    break;
                case KVType.STRING:
                    stringBuilder.Append("\"");
                    stringBuilder.Append(EscapeUnescaped((string)value, '"'));
                    stringBuilder.Append("\"");
                    break;
                case KVType.STRING_MULTI:
                    stringBuilder.Append("\"\"\"\n");
                    stringBuilder.Append((string)value);
                    stringBuilder.Append("\n\"\"\"");
                    break;
                case KVType.BOOLEAN:
                    stringBuilder.Append((bool)value ? "true" : "false");
                    break;
                case KVType.DOUBLE:
                    stringBuilder.Append(((double)value).ToString("#0.000000", CultureInfo.InvariantCulture));
                    break;
                case KVType.INTEGER:
                    stringBuilder.Append((int)value);
                    break;
                default:
                    //Unknown type encountered
                    throw new InvalidOperationException("Trying to print unknown type.");
            }
        }

        private void PrintIndent(StringBuilder stringBuilder, int num)
        {
            for (int i = 0; i < num; i++)
            {
                stringBuilder.Append("\t");
            }
        }
    }

    //Class to hold type + value
    public class KVValue
    {
        public KVType Type;
        public object Value;

        public KVValue(KVType type, object value)
        {
            Type = type;
            Value = value;
        }
    }
}
