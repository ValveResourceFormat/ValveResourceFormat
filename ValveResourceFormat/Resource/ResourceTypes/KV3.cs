/*
 * KV3Reader class.
 * This class reads in valve KV3 files and stores them in its datastructure.
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

namespace ValveKV
{
    public static class KV3Reader
    {
        private enum State {
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

        private const String whiteSpace = " \n\r\t";
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
            if (c == '>' && currentString.ToString().Substring( currentString.Length - 3 ) == "-->") {
                stateStack.Pop();
                stateStack.Push(State.SEEK_VALUE);
                return;
            }
        }

        //Seeking value state
        private static void SeekValue(char c)
        {
            //Ignore whitespace
            if (whiteSpace.Contains(c) || c == '=' )
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

                objStack.Push(new KVArray(currentString.ToString()));
            }
            //Check for array closing
            else if (c == ']')
            {
                stateStack.Pop();
                stateStack.Pop();

                KVObject value = objStack.Pop();
                objStack.Peek().AddProperty(value.key, new KVValue(typeof(KVObject),value));
            }
            //String opening
            else if (c == '"')
            {
                stateStack.Pop();
                stateStack.Push(State.VALUE_STRING);
                currentString.Clear();
            }
            //Booleans
            else if ((c == 'f' && ( fileStream[index+1] == 'a' && fileStream[index+2] == 'l' && fileStream[index+3] == 's' && fileStream[index+4] == 'e' )) 
                || (c == 't' && (fileStream[index+1] == 'r' && fileStream[index+2] == 'u' && fileStream[index+3] == 'e')))
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
            if (whiteSpace.Contains(c))
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
            if (whiteSpace.Contains(c))
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
                objStack.Peek().AddProperty(value.key, new KVValue(typeof(KVObject), value));
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
            if (c == '"')
            {
                //Multiline string detected
                if (currentString.ToString() == "\"")
                {
                    stateStack.Pop();
                    stateStack.Push(State.VALUE_STRING_MULTI);
                    currentString.Clear();
                    return;
                }
                //String ending found
                else if (currentString.Length > 0)
                {
                    stateStack.Pop();
                    objStack.Peek().AddProperty(currentName, new KVValue(typeof(String), currentString.ToString()));
                    return;
                }
            }

            currentString.Append(c);
        }

        //Reading multiline string
        private static void ReadValueStringMulti(char c)
        {
            //Check for ending
            if (c == '"' && currentString.ToString().Substring(currentString.Length - 2) == "\"\"")
            {
                stateStack.Pop();
                objStack.Peek().AddProperty(currentName, new KVValue(typeof(String), currentString.ToString().Substring(0, currentString.Length - 2)));
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
                objStack.Peek().AddProperty(currentName, new KVValue(typeof(Boolean), currentString.ToString() == "true" ? true : false));
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
                if ( currentString.ToString().Contains('.')) {
                    objStack.Peek().AddProperty(currentName, new KVValue(typeof(Double), Double.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                } else {
                    objStack.Peek().AddProperty(currentName, new KVValue(typeof(Int32), Int32.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                return;
            }

            //Stop reading the number once whtiespace is encountered
            if (whiteSpace.Contains(c))
            {
                stateStack.Pop();
                //Distinguish between doubles and ints
                if (currentString.ToString().Contains('.'))
                {
                    objStack.Peek().AddProperty(currentName, new KVValue(typeof(Double), Double.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                else
                {
                    objStack.Peek().AddProperty(currentName, new KVValue(typeof(Int32), Int32.Parse(currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                return;
            }

            currentString.Append(c);
        }

        //Read an array
        private static void ReadValueArray(char c)
        {
            //This shouldn't happen
            if (!whiteSpace.Contains(c) && c != ',')
            {
                Console.WriteLine("ERROR");
            }

            //Just jump to seek_value state
            stateStack.Push(State.SEEK_VALUE);
        }

        //Read a flagged value
        private static void ReadValueFlagged(char c)
        {
            //End at whitespace
            if (whiteSpace.Contains(c))
            {
                stateStack.Pop();
                objStack.Peek().AddProperty(currentName, new KVValue(typeof(FlaggedString), currentString.ToString()));
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
            if (c == '\n') {
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
        public String Serialize()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(Header);
            Root.Serialize(stringBuilder, 1);
            return stringBuilder.ToString();
        }
    }

    //Datastructure for a KV Object
    public class KVObject
    {
        public String key;
        public Dictionary<String, KVValue> Properties;

        public KVObject(String name)
        {
            key = name;
            Properties = new Dictionary<String, KVValue>();
        }

        //Add a property to the structure
        public virtual void AddProperty(String name, KVValue value)
        {
            Properties.Add(name, value);
        }

        //Serialize the contents of the KV object
        public virtual void Serialize(StringBuilder stringBuilder, int indent)
        {
            stringBuilder.Append("{\n");

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

            for (var i = 0; i < indent-1; i++)
            {
                stringBuilder.Append("\t");
            }
            stringBuilder.Append("}");
        }

        //Print a value in the correct representation
        protected void PrintValue(StringBuilder stringBuilder, Type type, Object value, int indent) {
            if (type == typeof(KVObject))
            {
                ((KVObject)value).Serialize(stringBuilder, indent + 1);
            }
            else if (type == typeof(FlaggedString))
            {
                stringBuilder.Append((String)value);
            }
            else if (type == typeof(String))
            {
                String str = (String)value;

                if (str.Contains("\n"))
                {
                    stringBuilder.Append("\"\"\"");
                    stringBuilder.Append(str);
                    stringBuilder.Append("\"\"\"");
                }
                else
                {
                    stringBuilder.Append("\"");
                    stringBuilder.Append(str);
                    stringBuilder.Append("\"");
                }
            }
            else if (type == typeof(Boolean))
            {
                if ((Boolean)value)
                {
                    stringBuilder.Append("true");
                }
                else
                {
                    stringBuilder.Append("false");
                }
            }
            else if (type == typeof(Double))
            {
                stringBuilder.Append(((Double)value).ToString("#0.000000",CultureInfo.InvariantCulture));
            }
            else if (type == typeof(Int32))
            {
                stringBuilder.Append((Int32)value);
            }
            else
            {
                //This shouldn't happen
                stringBuilder.Append(value);
            }
        }
    }

    //KV Array extending the KVObject
    public class KVArray : KVObject
    {
        new public List<KVValue> Properties;

        public KVArray(String name) : base(name)
        {
            Properties = new List<KVValue>();
        }

        public override void AddProperty(String name, KVValue value)
        {
            Properties.Add(value);
        }

        public override void Serialize(StringBuilder stringBuilder, int indent)
        {
            stringBuilder.Append(Environment.NewLine);
            for (var i = 0; i < indent-1; i++)
            {
                stringBuilder.Append("\t");
            }
            stringBuilder.Append("[");
            stringBuilder.Append(Environment.NewLine);

            foreach (KVValue entry in Properties) {
                for (var i = 0; i < indent; i++)
                {
                    stringBuilder.Append("\t");
                }

                PrintValue(stringBuilder, entry.Type, entry.Value, indent);

                stringBuilder.Append(",");
                stringBuilder.Append(Environment.NewLine);
            }

            for (var i = 0; i < indent - 1; i++)
            {
                stringBuilder.Append("\t");
            }
            stringBuilder.AppendLine("]");
        }
    }

    //Class to hold type + value
    public class KVValue
    {
        public Type Type;
        public Object Value;

        public KVValue(Type type, Object value)
        {
            Type = type;
            Value = value;
        }
    }

    //Empty class to keep track of flagged strings
    //This could probably be done better but meh.
    public class FlaggedString { }
}
