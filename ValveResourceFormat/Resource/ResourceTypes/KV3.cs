/*
 * KeyValues3 class.
 * This class reads in Valve KV3 files and stores them in its datastructure.
 *  
 * Interface:
 *  KVFile file = KV3Reader.ParseKVFile( fileName );
 *  String fileString = file.Serialize();
 *  
 * TODO:
 *  - Test some more and find the bugs.
 *  - Revisit state machine if bugs require it.
 *  - Improve KVFile interface.
 *  
 * Author: Perry - https://github.com/Perryvw
 * 
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;

namespace ValveResourceFormat.KeyValues
{
    public static class KeyValues3
    {
        private enum State
        {
            HEADER,
            SEEK_VALUE,
            PROP_NAME,
            VALUE_STRUCT,
            VALUE_ARRAY,
            VALUE_STRING,
            VALUE_STRING_MULTI,
            VALUE_NUMBER,
            VALUE_BOOL,
            VALUE_FLAGGED,
            COMMENT,
            COMMENT_BLOCK
        }

        private static char[] fileStream;
        private static int index;

        private static KVObject root = null;

        private static String currentName;
        private static StringBuilder currentString;

        private static Stack<KVObject> objStack;
        private static Stack<State> stateStack;

        private const String numerals = "-0123456789.";

        public static KVFile ParseKVFile(string filename)
        {
            //Initialise datastructures
            objStack = new Stack<KVObject>();
            stateStack = new Stack<State>();
            stateStack.Push(State.HEADER);

            root = new KVObject("root");
            objStack.Push(root);

            currentString = new StringBuilder();

            //Open stream
            fileStream = File.ReadAllText(filename).ToCharArray();
            index = 0;

            char c;
            while (index < fileStream.Length)
            {
                //Read character
                c = fileStream[index];

                //Do something depending on the current state
                switch (stateStack.Peek())
                {
                    case State.HEADER:
                        ReadHeader(c);
                        break;
                    case State.PROP_NAME:
                        ReadPropName(c);
                        break;
                    case State.SEEK_VALUE:
                        SeekValue(c);
                        break;
                    case State.VALUE_STRUCT:
                        ReadValueStruct(c);
                        break;
                    case State.VALUE_STRING:
                        ReadValueString(c);
                        break;
                    case State.VALUE_STRING_MULTI:
                        ReadValueStringMulti(c);
                        break;
                    case State.VALUE_BOOL:
                        ReadValueBool(c);
                        break;
                    case State.VALUE_NUMBER:
                        ReadValueNumber(c);
                        break;
                    case State.VALUE_ARRAY:
                        ReadValueArray(c);
                        break;
                    case State.VALUE_FLAGGED:
                        ReadValueFlagged(c);
                        break;
                    case State.COMMENT:
                        ReadComment(c);
                        break;
                    case State.COMMENT_BLOCK:
                        ReadCommentBlock(c);
                        break;
                }

                //Advance read index
                index++;
            }

            return new KVFile(root.Properties.ElementAt(0).Key, (KVObject)root.Properties.ElementAt(0).Value.Value);
        }

        //header state
        private static void ReadHeader(char c)
        {
            currentString.Append(c);

            //Read until --> is encountered
            if (c == '>' && currentString.ToString().Substring(currentString.Length - 3) == "-->")
            {
                stateStack.Pop();
                stateStack.Push(State.SEEK_VALUE);
                return;
            }
        }

        //Seeking value state
        private static void SeekValue(char c)
        {
            //Ignore whitespace
            if (Char.IsWhiteSpace(c) || c == '=')
            {
                return;
            }

            //Check struct opening
            if (c == '{')
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_STRUCT);

                objStack.Push(new KVObject(currentString.ToString()));
            }
            //Check for array opening
            else if (c == '[')
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_ARRAY);
                stateStack.Push(State.SEEK_VALUE);

                objStack.Push(new KVObject(currentString.ToString(), true));
            }
            //Check for array closing
            else if (c == ']')
            {
                stateStack.Pop();
                stateStack.Pop();

                KVObject value = objStack.Pop();
                objStack.Peek().AddProperty(value.Key, new KVValue(KVType.ARRAY, value));
            }
            //Multiline string opening
            else if (c == '"' && fileStream[index + 1] == '"' && fileStream[index + 2] == '"')
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_STRING_MULTI);
                currentString.Clear();

                //Skip to the start of the string
                index += 2;
            }
            //String opening
            else if (c == '"')
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_STRING);
                currentString.Clear();
            }
            //Booleans
            else if ((c == 'f' && (fileStream[index + 1] == 'a' && fileStream[index + 2] == 'l' && fileStream[index + 3] == 's' && fileStream[index + 4] == 'e'))
                || (c == 't' && (fileStream[index + 1] == 'r' && fileStream[index + 2] == 'u' && fileStream[index + 3] == 'e')))
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_BOOL);
                currentString.Clear();
                currentString.Append(c);
            }
            //Number
            else if (numerals.Contains(c))
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_NUMBER);
                currentString.Clear();
                currentString.Append(c);
            }
            //Flagged resource
            else
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_FLAGGED);
                currentString.Clear();
                currentString.Append(c);
            }
        }

        //Reading a property name
        private static void ReadPropName(char c)
        {
            //Stop once whitespace is encountered
            if (Char.IsWhiteSpace(c))
            {
                stateStack.Pop();
                stateStack.Push(State.SEEK_VALUE);
                currentName = currentString.ToString();
                return;
            }

            currentString.Append(c);
        }

        //Read a structure
        private static void ReadValueStruct(char c)
        {
            //Ignore whitespace
            if (Char.IsWhiteSpace(c))
            {
                return;
            }

            //Catch comments
            if (c == '/')
            {
                stateStack.Push(State.COMMENT);
                currentString.Clear();
                currentString.Append(c);
                return;
            }

            //Check for the end of the structure
            if (c == '}')
            {
                KVObject value = objStack.Pop();
                objStack.Peek().AddProperty(value.Key, new KVValue(KVType.OBJECT, value));
                stateStack.Pop();
                return;
            }

            //Start looking for the next property name
            stateStack.Push(State.PROP_NAME);
            currentString.Clear();
            currentString.Append(c);
        }

        //Read a string value
        private static void ReadValueString(char c)
        {
            if (c == '"' && fileStream[index - 1] != '\\')
            {
                //String ending found
                stateStack.Pop();
                objStack.Peek().AddProperty(currentName, new KVValue(KVType.STRING, currentString.ToString()));
                return;
            }

            currentString.Append(c);
        }

        //Reading multiline string
        private static void ReadValueStringMulti(char c)
        {
            //Check for ending
            if (c == '"' && c == '"' && fileStream[index + 1] == '"' && fileStream[index + 2] == '"' && fileStream[index - 1] != '\\')
            {
                stateStack.Pop();
                objStack.Peek().AddProperty(currentName, new KVValue(KVType.STRING_MULTI, currentString.ToString()));

                //Skip to end
                index += 2;
                return;
            }

            currentString.Append(c);
        }

        //Read a boolean variable
        private static void ReadValueBool(char c)
        {
            //Stop reading once the end of true or false is reached
            if (c == 'e')
            {
                currentString.Append(c);
                stateStack.Pop();
                objStack.Peek().AddProperty(currentName, new KVValue(KVType.BOOLEAN, currentString.ToString() == "true" ? true : false));
                return;
            }

            currentString.Append(c);
        }

        //Read a numerical value
        private static void ReadValueNumber(char c)
        {
            //For arrays
            if (c == ',')
            {
                stateStack.Pop();
                stateStack.Push(State.SEEK_VALUE);
                if (currentString.ToString().Contains('.'))
                {
                    objStack.Peek().AddProperty(currentName, new KVValue(KVType.DOUBLE, Double.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                else
                {
                    objStack.Peek().AddProperty(currentName, new KVValue(KVType.INTEGER, Int32.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                return;
            }

            //Stop reading the number once whtiespace is encountered
            if (Char.IsWhiteSpace(c))
            {
                stateStack.Pop();
                //Distinguish between doubles and ints
                if (currentString.ToString().Contains('.'))
                {
                    objStack.Peek().AddProperty(currentName, new KVValue(KVType.DOUBLE, Double.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                else
                {
                    objStack.Peek().AddProperty(currentName, new KVValue(KVType.INTEGER, Int32.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                return;
            }

            currentString.Append(c);
        }

        //Read an array
        private static void ReadValueArray(char c)
        {
            //This shouldn't happen
            if (!Char.IsWhiteSpace(c) && c != ',')
            {
                throw new Exception("Error parsing array.");
            }

            //Just jump to seek_value state
            stateStack.Push(State.SEEK_VALUE);
        }

        //Read a flagged value
        private static void ReadValueFlagged(char c)
        {
            //End at whitespace
            if (Char.IsWhiteSpace(c))
            {
                stateStack.Pop();
                objStack.Peek().AddProperty(currentName, new KVValue(KVType.FLAGGED_STRING, currentString.ToString()));
                return;
            }

            currentString.Append(c);
        }

        //Read comments
        private static void ReadComment(char c)
        {
            //Check for multiline comments
            if (currentString.Length == 1 && c == '*')
            {
                stateStack.Pop();
                stateStack.Push(State.COMMENT_BLOCK);
            }

            //Check for the end of a comment
            if (c == '\n')
            {
                stateStack.Pop();
                return;
            }

            if (c != '\r')
            {
                currentString.Append(c);
            }
        }

        //Read a comment block
        private static void ReadCommentBlock(char c)
        {
            //Look for the end of the comment block
            if (c == '/' && currentString.ToString().Last() == '*')
            {
                stateStack.Pop();
            }

            currentString.Append(c);
        }
    }

    //Data structure to contain the file contents
    public class KVFile
    {
        public String Header;
        public KVObject Root;

        public KVFile(String header, KVObject root)
        {
            this.Header = header;
            this.Root = root;
        }

        //Serialize the contents of the data structure
        public string Serialize()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(Header);
            Root.Serialize(stringBuilder, 1);
            return stringBuilder.ToString();
        }
    }

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
            stringBuilder.Append("{");
            stringBuilder.Append(Environment.NewLine);

            foreach (var pair in Properties)
            {
                for (var i = 0; i < indent; i++)
                {
                    stringBuilder.Append("\t");
                }

                stringBuilder.Append(pair.Key);
                stringBuilder.Append(" = ");

                PrintValue(stringBuilder, pair.Value.Type, pair.Value.Value, indent);

                stringBuilder.Append(Environment.NewLine);
            }

            for (var i = 0; i < indent - 1; i++)
            {
                stringBuilder.Append("\t");
            }
            stringBuilder.Append("}");
        }

        private void SerializeArray(StringBuilder stringBuilder, int indent)
        {
            stringBuilder.Append("[");
            stringBuilder.Append(Environment.NewLine);

            //Need to preserve the order
            for (var i = 0; i < Count; i++)
            {
                for (var j = 0; j < indent; j++)
                {
                    stringBuilder.Append("\t");
                }

                PrintValue(stringBuilder, Properties[i.ToString()].Type, Properties[i.ToString()].Value, indent);

                stringBuilder.Append(",");
                stringBuilder.Append(Environment.NewLine);
            }

            for (var i = 0; i < indent - 1; i++)
            {
                stringBuilder.Append("\t");
            }
            stringBuilder.AppendLine("]");
        }

        //Print a value in the correct representation
        private void PrintValue(StringBuilder stringBuilder, KVType type, object value, int indent)
        {
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
                    stringBuilder.Append((string)value);
                    stringBuilder.Append("\"");
                    break;
                case KVType.STRING_MULTI:
                    stringBuilder.Append("\"\"\"");
                    stringBuilder.Append((string)value);
                    stringBuilder.Append("\"\"\"");
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
                    throw new Exception("Unknown type encountered.");
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
