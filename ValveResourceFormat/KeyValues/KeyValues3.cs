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
            VALUE_FLAGGED,
            COMMENT,
            COMMENT_BLOCK
        }

        private class Parser
        {
            public StreamReader fileStream;

            public KVObject root = null;

            public String currentName;
            public StringBuilder currentString;

            public char previousChar;
            public Queue<char> charBuffer;

            public Stack<KVObject> objStack;
            public Stack<State> stateStack;

            public Parser()
            {
                //Initialise datastructures
                objStack = new Stack<KVObject>();
                stateStack = new Stack<State>();
                stateStack.Push(State.HEADER);

                root = new KVObject("root");
                objStack.Push(root);

                previousChar = '\0';
                charBuffer = new Queue<char>();

                currentString = new StringBuilder();
            }
        }

        private const String numerals = "-0123456789.";

        public static KVFile ParseKVFile(string filename)
        {
            using (var fileStream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                return ParseKVFile(fileStream);
            }
        }

        public static KVFile ParseKVFile(Stream fileStream)
        {
            var parser = new Parser();

            //Open stream reader
            parser.fileStream = new StreamReader(fileStream);

            char c;
            while (!parser.fileStream.EndOfStream)
            {
                c = NextChar(parser);

                //Do something depending on the current state
                switch (parser.stateStack.Peek())
                {
                    case State.HEADER:
                        ReadHeader(c, parser);
                        break;
                    case State.PROP_NAME:
                        ReadPropName(c, parser);
                        break;
                    case State.SEEK_VALUE:
                        SeekValue(c, parser);
                        break;
                    case State.VALUE_STRUCT:
                        ReadValueStruct(c, parser);
                        break;
                    case State.VALUE_STRING:
                        ReadValueString(c, parser);
                        break;
                    case State.VALUE_STRING_MULTI:
                        ReadValueStringMulti(c, parser);
                        break;
                    case State.VALUE_NUMBER:
                        ReadValueNumber(c, parser);
                        break;
                    case State.VALUE_ARRAY:
                        ReadValueArray(c, parser);
                        break;
                    case State.VALUE_FLAGGED:
                        ReadValueFlagged(c, parser);
                        break;
                    case State.COMMENT:
                        ReadComment(c, parser);
                        break;
                    case State.COMMENT_BLOCK:
                        ReadCommentBlock(c, parser);
                        break;
                }

                parser.previousChar = c;
            }

            return new KVFile(parser.root.Properties.ElementAt(0).Key, (KVObject)parser.root.Properties.ElementAt(0).Value.Value);
        }

        //header state
        private static void ReadHeader(char c, Parser parser)
        {
            parser.currentString.Append(c);

            //Read until --> is encountered
            if (c == '>' && parser.currentString.ToString().Substring(parser.currentString.Length - 3) == "-->")
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.SEEK_VALUE);
                return;
            }
        }

        //Seeking value state
        private static void SeekValue(char c, Parser parser)
        {
            //Ignore whitespace
            if (Char.IsWhiteSpace(c) || c == '=')
            {
                return;
            }

            //Check struct opening
            if (c == '{')
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.VALUE_STRUCT);

                parser.objStack.Push(new KVObject(parser.currentString.ToString()));
            }
            //Check for array opening
            else if (c == '[')
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.VALUE_ARRAY);
                parser.stateStack.Push(State.SEEK_VALUE);

                parser.objStack.Push(new KVObject(parser.currentString.ToString(), true));
            }
            //Check for array closing
            else if (c == ']')
            {
                parser.stateStack.Pop();
                parser.stateStack.Pop();

                KVObject value = parser.objStack.Pop();
                parser.objStack.Peek().AddProperty(value.Key, new KVValue(KVType.ARRAY, value));
            }
            //String opening
            else if (c == '"')
            {
                //Check if a multistring or single string was found
                string next = PeekString(parser, 4);
                if (next.Contains("\"\"\n") || next == "\"\"\r\n")
                {
                    //Skip the next two "'s
                    SkipChars(parser, 2);

                    parser.stateStack.Pop();
                    parser.stateStack.Push(State.VALUE_STRING_MULTI);
                    parser.currentString.Clear();
                }
                else
                {
                    parser.stateStack.Pop();
                    parser.stateStack.Push(State.VALUE_STRING);
                    parser.currentString.Clear();
                }
            }
            //Boolean false
            else if (c == 'f' && PeekString(parser, 4) == "alse")
            {
                parser.stateStack.Pop();
                
                //Can directly be added
                parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.BOOLEAN, false));

                //Skip next characters
                SkipChars(parser, 4);
            }
            //Boolean true
            else if (c == 'f' && PeekString(parser, 3) == "rue")
            {
                parser.stateStack.Pop();

                //Can directly be added
                parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.BOOLEAN, true));

                //Skip next characters
                SkipChars(parser, 3);
            }
            //Number
            else if (numerals.Contains(c))
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.VALUE_NUMBER);
                parser.currentString.Clear();
                parser.currentString.Append(c);
            }
            //Flagged resource
            else
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.VALUE_FLAGGED);
                parser.currentString.Clear();
                parser.currentString.Append(c);
            }
        }

        //Reading a property name
        private static void ReadPropName(char c, Parser parser)
        {
            //Stop once whitespace is encountered
            if (Char.IsWhiteSpace(c))
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.SEEK_VALUE);
                parser.currentName = parser.currentString.ToString();
                return;
            }

            parser.currentString.Append(c);
        }

        //Read a structure
        private static void ReadValueStruct(char c, Parser parser)
        {
            //Ignore whitespace
            if (Char.IsWhiteSpace(c))
            {
                return;
            }

            //Catch comments
            if (c == '/')
            {
                parser.stateStack.Push(State.COMMENT);
                parser.currentString.Clear();
                parser.currentString.Append(c);
                return;
            }

            //Check for the end of the structure
            if (c == '}')
            {
                KVObject value = parser.objStack.Pop();
                parser.objStack.Peek().AddProperty(value.Key, new KVValue(KVType.OBJECT, value));
                parser.stateStack.Pop();
                return;
            }

            //Start looking for the next property name
            parser.stateStack.Push(State.PROP_NAME);
            parser.currentString.Clear();
            parser.currentString.Append(c);
        }

        //Read a string value
        private static void ReadValueString(char c, Parser parser)
        {
            if (c == '"' && parser.previousChar != '\\')
            {
                //String ending found
                parser.stateStack.Pop();
                parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.STRING, parser.currentString.ToString()));
                return;
            }

            parser.currentString.Append(c);
        }

        //Reading multiline string
        private static void ReadValueStringMulti(char c, Parser parser)
        {
            //Check for ending
            string next = PeekString(parser, 2);
            if (c == '"' && next == "\"\"" && parser.previousChar != '\\')
            {
                //Check for starting and trailing linebreaks
                string multilineStr = parser.currentString.ToString();
                int start = 0;
                int end = multilineStr.Length;

                //Check the start
                if (multilineStr.ElementAt(0) == '\n')
                {
                    start = 1;
                }
                else if (multilineStr.ElementAt(0) == '\r' && multilineStr.ElementAt(1) == '\n')
                {
                    start = 2;
                }

                //Check the end
                if (multilineStr.ElementAt(multilineStr.Length - 1) == '\n')
                {
                    if (multilineStr.ElementAt(multilineStr.Length - 2) == '\r')
                    {
                        end = multilineStr.Length - 1;
                    }
                    else
                    {
                        end = multilineStr.Length - 1;
                    }
                }

                multilineStr = multilineStr.Substring(start, end - start);

                //Set parser state
                parser.stateStack.Pop();
                parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.STRING_MULTI, multilineStr));

                //Skip to end of the block
                SkipChars(parser, 2);
                return;
            }

            parser.currentString.Append(c);
        }

        //Read a numerical value
        private static void ReadValueNumber(char c, Parser parser)
        {
            //For arrays
            if (c == ',')
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.SEEK_VALUE);
                if (parser.currentString.ToString().Contains('.'))
                {
                    parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.DOUBLE, Double.Parse(parser.currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                else
                {
                    parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.INTEGER, Int32.Parse(parser.currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                return;
            }

            //Stop reading the number once whtiespace is encountered
            if (Char.IsWhiteSpace(c))
            {
                parser.stateStack.Pop();
                //Distinguish between doubles and ints
                if (parser.currentString.ToString().Contains('.'))
                {
                    parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.DOUBLE, Double.Parse(parser.currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                else
                {
                    parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.INTEGER, Int32.Parse(parser.currentString.ToString(), CultureInfo.InvariantCulture)));
                }
                return;
            }

            parser.currentString.Append(c);
        }

        //Read an array
        private static void ReadValueArray(char c, Parser parser)
        {
            //This shouldn't happen
            if (!Char.IsWhiteSpace(c) && c != ',')
            {
                throw new Exception("Error parsing array.");
            }

            //Just jump to seek_value state
            parser.stateStack.Push(State.SEEK_VALUE);
        }

        //Read a flagged value
        private static void ReadValueFlagged(char c, Parser parser)
        {
            //End at whitespace
            if (Char.IsWhiteSpace(c))
            {
                parser.stateStack.Pop();
                parser.objStack.Peek().AddProperty(parser.currentName, new KVValue(KVType.FLAGGED_STRING, parser.currentString.ToString()));
                return;
            }

            parser.currentString.Append(c);
        }

        //Read comments
        private static void ReadComment(char c, Parser parser)
        {
            //Check for multiline comments
            if (parser.currentString.Length == 1 && c == '*')
            {
                parser.stateStack.Pop();
                parser.stateStack.Push(State.COMMENT_BLOCK);
            }

            //Check for the end of a comment
            if (c == '\n')
            {
                parser.stateStack.Pop();
                return;
            }

            if (c != '\r')
            {
                parser.currentString.Append(c);
            }
        }

        //Read a comment block
        private static void ReadCommentBlock(char c, Parser parser)
        {
            //Look for the end of the comment block
            if (c == '/' && parser.currentString.ToString().Last() == '*')
            {
                parser.stateStack.Pop();
            }

            parser.currentString.Append(c);
        }

        //Get the next char from the filestream
        private static char NextChar(Parser parser)
        {
            //Check if there are characters in the buffer, otherwise read a new one
            if (parser.charBuffer.Count > 0)
            {
                return parser.charBuffer.Dequeue();
            }
            else
            {
                return (char)parser.fileStream.Read();
            }
        }

        //Skip the next X characters in the filestream
        private static void SkipChars(Parser parser, int num)
        {
            for (int i = 0; i < num; i++)
            {
                NextChar(parser);
            }
        }

        //Utility function
        private static string PeekString(Parser parser, int length)
        {
            char[] buffer = new char[length];            
            for (int i = 0; i < length; i++)
            {
                if (i < parser.charBuffer.Count)
                {
                    buffer[i] = parser.charBuffer.ElementAt(i);
                }
                else
                {
                    buffer[i] = (char)parser.fileStream.Read();
                    parser.charBuffer.Enqueue(buffer[i]);
                }
            }
            return String.Join("", buffer);
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
}
