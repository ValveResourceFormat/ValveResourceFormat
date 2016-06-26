using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;

namespace ValveResourceFormat.ResourceTypes.NTROSerialization
{
    public class NTROArray : NTROValue, IList<NTROValue>
    {
        private readonly NTROValue[] contents;
        public bool IsIndirection { get; private set; }

        public NTROArray(DataType type, int count, bool pointer = false, bool isIndirection = false)
        {
            Type = type; // from NTROValue
            Pointer = pointer; // from NTROValue

            IsIndirection = isIndirection;

            contents = new NTROValue[count];
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            if (Count > 1)
            {
                throw new NotImplementedException("NTROArray.Count > 1");
            }

            foreach (var entry in this)
            {
                entry.WriteText(writer);
            }
        }

        public NTROValue this[int index]
        {
            get
            {
                return ((IList<NTROValue>)contents)[index];
            }
            set
            {
                ((IList<NTROValue>)contents)[index] = value;
            }
        }

        public int Count
        {
            get
            {
                return ((IList<NTROValue>)contents).Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return ((IList<NTROValue>)contents).IsReadOnly;
            }
        }

        public void Add(NTROValue item)
        {
            ((IList<NTROValue>)contents).Add(item);
        }

        public void Clear()
        {
            ((IList<NTROValue>)contents).Clear();
        }

        public bool Contains(NTROValue item)
        {
            return ((IList<NTROValue>)contents).Contains(item);
        }

        public void CopyTo(NTROValue[] array, int arrayIndex)
        {
            ((IList<NTROValue>)contents).CopyTo(array, arrayIndex);
        }

        public IEnumerator<NTROValue> GetEnumerator()
        {
            return ((IList<NTROValue>)contents).GetEnumerator();
        }

        public int IndexOf(NTROValue item)
        {
            return ((IList<NTROValue>)contents).IndexOf(item);
        }

        public void Insert(int index, NTROValue item)
        {
            ((IList<NTROValue>)contents).Insert(index, item);
        }

        public bool Remove(NTROValue item)
        {
            return ((IList<NTROValue>)contents).Remove(item);
        }

        public void RemoveAt(int index)
        {
            ((IList<NTROValue>)contents).RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IList<NTROValue>)contents).GetEnumerator();
        }

        public T Get<T>(int index)
        {
            return ((NTROValue<T>)this[index]).Value;
        }

        public T[] ToArray<T>()
        {
            var array = new T[Count];
            for (var i = 0; i < Count; i++)
            {
                array[i] = Get<T>(i);
            }

            return array;
        }
    }
}
