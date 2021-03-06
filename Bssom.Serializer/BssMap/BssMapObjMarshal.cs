﻿//using System.Runtime.CompilerServices;

using Bssom.Serializer.Binary;
using Bssom.Serializer.BssomBuffer;
using Bssom.Serializer.Internal;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Bssom.Serializer.BssMap
{
    /// <summary>
    /// Binary search algorithm structure model MapObject marshalling
    /// </summary>
    internal static class BssMapObjMarshal
    {
        public const int DefaultMapLengthFieldSize = BssomBinaryPrimitives.FixUInt32NumberSize;
        public const int DefaultRouteLengthFieldSize = BssomBinaryPrimitives.FixUInt32NumberSize;
        public static readonly byte[] Empty;

        static BssMapObjMarshal()
        {
            ExpandableBufferWriter bw = ExpandableBufferWriter.CreateTemporary();
            BssomWriter cw = new BssomWriter(bw);
            cw.WriteUInt32FixNumber(7);//reference DefaultMapLengthFieldSize
            cw.WriteVariableNumber(0);
            cw.WriteVariableNumber(0);
            cw.WriteUInt32FixNumber(0);//reference DefaultRouteLengthFieldSize
            Empty = bw.GetBufferedArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteEmptyMapObject(ref BssomWriter writer)
        {
            writer.WriteRaw(Empty, 0, Empty.Length);
        }

        public static long WriteMapHead(ref BssomWriter writer, int valueCount, int maxDepth)
        {
            DEBUG.Assert(valueCount != 0 && maxDepth != 0);
            writer.FillUInt32FixNumber();//len
            writer.WriteVariableNumber(valueCount);//count
            writer.WriteVariableNumber(maxDepth);//depth
            long routeLenPos = writer.Position;
            writer.FillUInt32FixNumber();//routelen
            return routeLenPos;
        }

        public static int SizeMapHead(int valueCount, int maxDepth)
        {
            DEBUG.Assert(valueCount != 0 && maxDepth != 0);
            return DefaultMapLengthFieldSize + BssomBinaryPrimitives.VariableNumberSize((ulong)valueCount) + BssomBinaryPrimitives.VariableNumberSize((ulong)maxDepth) + DefaultRouteLengthFieldSize;
        }

        public static unsafe string GetSchemaString(ref BssomReader reader)
        {
            long positionWithOutMap2TypeHead(ref BssomReader r)
            {
                return r.Position - 1;
            }

            MapSchemaStringBuilder msb = new MapSchemaStringBuilder();
            BssMapHead head = BssMapHead.Read(ref reader);
            byte nextKeyByteCount = 0;
            BssMapRouteToken t = default;
            int count = head.ElementCount;
            BssmapAnalysisStack stack = new BssmapAnalysisStack(head.MaxDepth);
            AutomateState state = AutomateState.ReadBranch;
            switch (state)
            {
                case AutomateState.ReadBranch:
                    {
                        t = reader.ReadMapToken();
                        msb.AppendRouteToken(positionWithOutMap2TypeHead(ref reader) - 1, t);
                        stack.PushToken(t);

                        if (t >= BssMapRouteToken.EqualNext1 && t <= BssMapRouteToken.EqualNext8 ||
                              t >= BssMapRouteToken.EqualLast1 && t <= BssMapRouteToken.EqualLast8)
                        {
                            if (t >= BssMapRouteToken.EqualNext1 && t <= BssMapRouteToken.EqualNext8)
                            {
                                long position = positionWithOutMap2TypeHead(ref reader);
                                reader.EnsureType(BssomBinaryPrimitives.FixUInt16);
                                msb.AppendNextOff(position, reader.ReadUInt16WithOutTypeHead());
                            }

                            nextKeyByteCount = BssMapRouteTokenHelper.GetEqualNextOrLastByteCount(t);
                            goto case AutomateState.ReadKey;
                        }
                        else if (t == BssMapRouteToken.EqualNextN || t == BssMapRouteToken.EqualLastN)
                        {
                            if (t == BssMapRouteToken.EqualNextN)
                            {
                                long position = positionWithOutMap2TypeHead(ref reader);
                                reader.EnsureType(BssomBinaryPrimitives.FixUInt16);
                                msb.AppendNextOff(position, reader.ReadUInt16WithOutTypeHead());
                            }

                            ulong uint64Val = reader.ReadUInt64WithOutTypeHead();
                            msb.AppendUInt64Val(positionWithOutMap2TypeHead(ref reader) - 8, uint64Val);

                            goto case AutomateState.ReadBranch;
                        }
                        else if (t >= BssMapRouteToken.LessThen1 && t <= BssMapRouteToken.LessThen8)
                        {
                            long position = positionWithOutMap2TypeHead(ref reader);
                            reader.EnsureType(BssomBinaryPrimitives.FixUInt16);
                            msb.AppendNextOff(position, reader.ReadUInt16WithOutTypeHead());

                            nextKeyByteCount = BssMapRouteTokenHelper.GetLessThenByteCount(t);

                            position = positionWithOutMap2TypeHead(ref reader);
                            if (nextKeyByteCount == 8)
                            {
                                msb.AppendUInt64Val(position, reader.ReadUInt64WithOutTypeHead());
                            }
                            else
                            {
                                msb.AppendUInt64Val(position, ref reader.BssomBuffer.ReadRef(nextKeyByteCount), nextKeyByteCount);
                                reader.BssomBuffer.SeekWithOutVerify(nextKeyByteCount, BssomSeekOrgin.Current);
                            }

                            goto case AutomateState.ReadBranch;
                        }
                        else if (t == BssMapRouteToken.LessElse)
                        {
                            goto case AutomateState.ReadBranch;
                        }
                        else
                        {
                            throw BssomSerializationOperationException.UnexpectedCodeRead((byte)t, reader.Position);
                        }
                    }
                case AutomateState.ReadKey:
                    {
                        if (nextKeyByteCount == 8)
                        {
                            ulong uint64Val = reader.ReadUInt64WithOutTypeHead();
                            msb.AppendUInt64Val(positionWithOutMap2TypeHead(ref reader) - 8, uint64Val);
                        }
                        else
                        {
                            //Read Raw(lessthan 8 byte)
                            ref byte ref1 = ref reader.BssomBuffer.ReadRef(nextKeyByteCount);
                            msb.AppendUInt64Val(positionWithOutMap2TypeHead(ref reader) - nextKeyByteCount, ref ref1, nextKeyByteCount);
                            reader.BssomBuffer.SeekWithOutVerify(nextKeyByteCount, BssomSeekOrgin.Current);
                        }
                        long position = positionWithOutMap2TypeHead(ref reader);
                        byte keyType = reader.ReadBssomType();
                        if (keyType == BssomType.NativeCode)
                        {
                            msb.AppendKeyType(position, true, reader.ReadBssomType());
                        }
                        else
                        {
                            msb.AppendKeyType(position, false, keyType);
                        }

                        position = positionWithOutMap2TypeHead(ref reader);
                        reader.EnsureType(BssomBinaryPrimitives.FixUInt32);
                        msb.AppendValOffset(position, reader.ReadUInt32WithOutTypeHead());

                        count--;
                        goto case AutomateState.ReadChildren;
                    }
                case AutomateState.ReadChildren:
                    {
                        t = reader.ReadMapToken();
                        msb.AppendRouteToken(positionWithOutMap2TypeHead(ref reader) - 1, t);

                        if (t == BssMapRouteToken.HasChildren)
                        {
                            goto case AutomateState.ReadBranch;
                        }
                        else if (t == BssMapRouteToken.NoChildren)
                        {
                        GO1:
                            t = stack.PeekToken();
                            if (t >= BssMapRouteToken.EqualNext1 && t <= BssMapRouteToken.EqualNextN)
                            {
                                stack.PopToken();
                                goto case AutomateState.ReadBranch;
                            }
                            else if (t >= BssMapRouteToken.EqualLast1 && t <= BssMapRouteToken.EqualLastN)
                            {
                                stack.PopToken();
                                if (stack.TokenCount > 0)
                                {
                                    goto GO1;
                                }
                                else
                                {
                                    goto case AutomateState.CheckEnd;
                                }
                            }
                            else if (t >= BssMapRouteToken.LessThen1 && t <= BssMapRouteToken.LessThen8)
                            {
                                stack.PopToken();
                                goto case AutomateState.ReadBranch;
                            }
                            else if (t == BssMapRouteToken.LessElse)
                            {
                                stack.PopToken();
                                if (stack.TokenCount > 0)
                                {
                                    goto GO1;
                                }

                                goto case AutomateState.CheckEnd;
                            }
                            else
                            {
                                throw BssomSerializationOperationException.UnexpectedCodeRead((byte)t, reader.Position);
                            }
                        }
                        else
                        {
                            throw BssomSerializationOperationException.UnexpectedCodeRead((byte)t, reader.Position);
                        }
                    }
                case AutomateState.CheckEnd:
                    {
                        if (count != 0)
                        {
                            goto case AutomateState.ReadBranch;
                        }
                        break;
                    }
            }
            return msb.ToString();
        }
    }

    internal struct BssMapObjMarshal<TValue>
    {
        //TODO: This structure will be changed the next time the code is refactored
        //Entry {
        //      IsKey, KeyInfo , CurrentUInt64, ChidlernInfo
        //  }
        public class Entry
        {
            public byte KeyType;
            public bool KeyIsNativeType;
            public TValue Value;
            public bool IsKey;
            public byte LastValueByteCount;
            public ulong CurrentUInt64Value;
            public Entry[] Chidlerns;
            public int ChidlernLength;

            public Entry()
            {
                KeyType = default;
                KeyIsNativeType = default;
                Value = default;
                IsKey = default;
                LastValueByteCount = 8;
                CurrentUInt64Value = default;
                Chidlerns = default;
                ChidlernLength = default;
            }

            public Entry Clone()
            {
                return new Entry()
                {
                    KeyType = KeyType,
                    KeyIsNativeType = KeyIsNativeType,
                    Value = Value,
                    IsKey = IsKey,
                    LastValueByteCount = LastValueByteCount,
                    CurrentUInt64Value = CurrentUInt64Value,
                    Chidlerns = Chidlerns,
                    ChidlernLength = ChidlernLength
                };
            }

            public Entry WithNotKey()
            {
                IsKey = false;
                LastValueByteCount = default;
                Value = default;
                KeyType = default;
                KeyIsNativeType = default;
                return this;
            }

            public Entry WithNotChildren()
            {
                Chidlerns = null;
                ChidlernLength = 0;
                return this;
            }

            public void SetKey(byte lastValueByteCount, TValue value, byte keyType, bool isNativeType)
            {
                IsKey = true;
                LastValueByteCount = lastValueByteCount;
                Value = value;
                KeyType = keyType;
                KeyIsNativeType = isNativeType;
            }
        }
        public class EntryCompare : IComparer<Entry>
        {
            public static EntryCompare Instance = new EntryCompare();
            public int Compare(Entry x, Entry y)
            {
                return x.CurrentUInt64Value.CompareTo(y.CurrentUInt64Value);
            }
        }

        public int Length { get; private set; }
        public Entry[] Entries { get; private set; }
        public ushort ValueCount { get; }
        public ushort MaxDepth { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BssMapObjMarshal(BssRow<TValue>[] rows)
        {
            ValueCount = (ushort)rows.Length;
            MaxDepth = Depth(rows);
            Entries = Generate(rows);
            Optimization(Entries);
            Length = LengthCounter(Entries);
            RecursiveLengthCounter(Entries, Length);
        }

        private static Entry[] Generate(BssRow<TValue>[] rows)
        {
            Entry[] entries = new Entry[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                IMapKeySegment ulongs = rows[i].Key;
                TValue value = rows[i].Value;
                byte keyType = rows[i].KeyType;
                bool keyIsNativeType = rows[i].KeyIsNativeType;

                Entry entry = new Entry();
                entries[i] = entry;
                for (int c = 0; c < ulongs.Length; c++)
                {
                    entry.CurrentUInt64Value = ulongs[c];
                    if (c == ulongs.Length - 1)
                    {
                        entry.SetKey((byte)ulongs.LastValueByteCount, value, keyType, keyIsNativeType);
                    }
                    else
                    {
                        entry.Chidlerns = new Entry[1] { new Entry() };
                        entry = entry.Chidlerns[0];
                    }
                }
            }
            return entries;
        }

        private static void Optimization(Entry[] entries)
        {
            // 3 
            // 3 4   i-1
            // 3 5   i
            // ==>  3(hasValue)
            //        Chidlren[1] : 4   hasValue
            //        Chidlren[2] : 5   hasValue
            Array.Sort(entries, EntryCompare.Instance);

            for (int i = entries.Length - 1; i >= 1; i--)
            {
                if (entries[i].CurrentUInt64Value == entries[i - 1].CurrentUInt64Value)
                {
                    if (entries[i].IsKey == true && entries[i - 1].IsKey == true)
                    {
                        throw BssomSerializationArgumentException.BssMapKeyUnsupportedSameRawValue(entries[i].KeyType, entries[i].KeyIsNativeType, entries[i - 1].KeyType, entries[i - 1].KeyIsNativeType, entries[i].CurrentUInt64Value);
                    }

                    if (entries[i].Chidlerns != null)
                    {
                        if (entries[i - 1].Chidlerns == null)
                        {
                            entries[i - 1].Chidlerns = entries[i].Chidlerns;
                        }
                        else
                        {
                            int len = entries[i].Chidlerns.Length;
                            Array.Resize(ref entries[i].Chidlerns, entries[i].Chidlerns.Length + entries[i - 1].Chidlerns.Length);
                            Array.Copy(entries[i - 1].Chidlerns, 0, entries[i].Chidlerns, len, entries[i - 1].Chidlerns.Length);
                            entries[i - 1].Chidlerns = entries[i].Chidlerns;
                        }
                    }

                    if (entries[i].IsKey)
                    {
                        entries[i - 1].SetKey(entries[i].LastValueByteCount, entries[i].Value, entries[i].KeyType, entries[i].KeyIsNativeType);
                    }
                    entries[i] = null;
                }
            }
            /*
         + 0
         - 1
         + 2
         + 3
         - 4
         - 5
         + 6
         */

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                {
                    for (int z = i + 1; z < entries.Length; z++)
                    {
                        if (entries[z] != null)
                        {
                            entries[i] = entries[z];
                            entries[z] = null;
                            i++;
                        }
                    }
                    break;
                }
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] == null)
                {
                    break;
                }

                if (entries[i].Chidlerns != null)
                {
                    Optimization(entries[i].Chidlerns);
                }
            }

        }

        private static int LengthCounter(Entry[] entries)
        {
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i] != null)
                {
                    return i + 1;
                }
            }
            return 0;
        }

        private static void RecursiveLengthCounter(Entry[] entries, int len)
        {
            for (int i = 0; i < len; i++)
            {
                if (entries[i].Chidlerns != null)
                {
                    entries[i].ChidlernLength = LengthCounter(entries[i].Chidlerns);
                    RecursiveLengthCounter(entries[i].Chidlerns, entries[i].ChidlernLength);
                }

            }
        }

        private static ushort Depth(BssRow<TValue>[] rows)
        {
            int max = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Key.Length > max)
                {
                    max = rows[i].Key.Length;
                }
            }

            return (ushort)max;
        }

        public unsafe void Write(ref BssomWriter writer, ref BssomSerializeContext context)
        {
            IBssomFormatter<TValue> formatter = context.Option.FormatterResolver.GetFormatterWithVerify<TValue>();

            if (ValueCount == 0)
            {
                BssMapObjMarshal.WriteEmptyMapObject(ref writer);
                return;
            }

            long startPos = writer.Position;
            Queue<KeyValuePair<int, TValue>> valueMapOffsets = WriteHeader(ref writer);
            BssMapWriteBackEntry* ptr = stackalloc BssMapWriteBackEntry[valueMapOffsets.Count];
            StackArrayPack<BssMapWriteBackEntry> writeBacks = new StackArrayPack<BssMapWriteBackEntry>(ptr, valueMapOffsets.Count);
            WriteValues(ref writer, formatter, startPos, ref context, valueMapOffsets, ref writeBacks);
            WriteBackHeader(ref writer, startPos, ref writeBacks);
        }

        public unsafe int Size(ref BssomSizeContext context)
        {
            IBssomFormatter<TValue> formatter = context.Option.FormatterResolver.GetFormatterWithVerify<TValue>();

            if (ValueCount == 0)
            {
                return BssMapObjMarshal.Empty.Length;
            }

            int len = SizeHeader(out Queue<KeyValuePair<int, TValue>> valueMapOffsets);
            while (valueMapOffsets.Count > 0)
            {
                KeyValuePair<int, TValue> item = valueMapOffsets.Dequeue();
                len += formatter.Size(ref context, item.Value);
            }

            return len;
        }

        //key:map-ValueOffset  value:Value
        public Queue<KeyValuePair<int, TValue>> WriteHeader(ref BssomWriter writer)
        {
            if (ValueCount == 0)
            {
                BssMapObjMarshal.WriteEmptyMapObject(ref writer);
                return new Queue<KeyValuePair<int, TValue>>();
            }

            long startPos = writer.Position;
            long routeLenPos = BssMapObjMarshal.WriteMapHead(ref writer, ValueCount, MaxDepth);
            Queue<KeyValuePair<int, TValue>> valueMapOffsets = new Queue<KeyValuePair<int, TValue>>();
            MapRouteSegmentWriter mrsWriter = new MapRouteSegmentWriter(writer, startPos);
            mrsWriter.WriteRouteData(Entries, 0, Length, valueMapOffsets);
            writer = mrsWriter.GetBssomWriter();
            writer.WriteBackFixNumber(routeLenPos, checked((int)(writer.Position - routeLenPos - BssMapObjMarshal.DefaultRouteLengthFieldSize)));
            return valueMapOffsets;
        }

        private int SizeHeader(out Queue<KeyValuePair<int, TValue>> valueMapOffsets)
        {
            valueMapOffsets = new Queue<KeyValuePair<int, TValue>>();
            MapMetaSegmentWriterAux mmsAux = new MapMetaSegmentWriterAux();
            mmsAux.SizeHeadData(ValueCount, MaxDepth);
            mmsAux.SizeRouteData(Entries, 0, Length, valueMapOffsets);
            return mmsAux.Advanced;
        }

        private static unsafe void WriteValues(ref BssomWriter writer, IBssomFormatter<TValue> formatter, long basePostion, ref BssomSerializeContext context, Queue<KeyValuePair<int, TValue>> valueMapOffsets, ref StackArrayPack<BssMapWriteBackEntry> writeBacks)
        {
            while (valueMapOffsets.Count > 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                KeyValuePair<int, TValue> item = valueMapOffsets.Dequeue();

                writeBacks.Add(new BssMapWriteBackEntry() { MapOffset = item.Key, ValueOffset = (uint)(writer.Position - basePostion) });
                formatter.Serialize(ref writer, ref context, item.Value);
            }
        }

        private static void WriteBackHeader(ref BssomWriter writer, long basePostion, ref StackArrayPack<BssMapWriteBackEntry> writeBacks)
        {
            long curPos = writer.Position;
            int len = checked((int)(curPos - basePostion));
            writer.BufferWriter.Seek(basePostion);
            writer.WriteBackFixNumber(len - BssMapObjMarshal.DefaultMapLengthFieldSize);//writeback maplength
            for (int i = 0; i < writeBacks.Length; i++)
            {
                writer.BufferWriter.Seek(basePostion + writeBacks[i].MapOffset);
                writer.WriteBackFixNumber(checked((int)writeBacks[i].ValueOffset));
            }
            writer.BufferWriter.Seek(curPos);
        }
    }
}
