﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    public sealed class ClrmdModule : ClrModule
    {
        private readonly IModuleHelpers _helpers;
        private int _debugMode = int.MaxValue;
        private MetadataImport? _metadata;
        private PdbInfo? _pdb;
        private IReadOnlyList<(ulong, uint)>? _typeDefMap;
        private IReadOnlyList<(ulong, uint)>? _typeRefMap;

        public override ClrAppDomain AppDomain { get; }
        public override string? Name { get; }
        public override string? AssemblyName { get; }
        public override ulong AssemblyAddress { get; }
        public override ulong Address { get; }
        public override bool IsPEFile { get; }
        public override ulong ImageBase { get; }
        public override bool IsFileLayout { get; }
        public override ulong Size { get; }
        public override ulong MetadataAddress { get; }
        public override ulong MetadataLength { get; }
        public override bool IsDynamic { get; }
        public override string? FileName => IsPEFile ? Name : null;
        public override MetadataImport? MetadataImport => _metadata ??= _helpers.GetMetadataImport(this);

        public ClrmdModule(ClrAppDomain parent, IModuleData data)
        {
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            _helpers = data.Helpers;
            AppDomain = parent;
            Name = data.Name;
            AssemblyName = data.AssemblyName;
            AssemblyAddress = data.AssemblyAddress;
            Address = data.Address;
            IsPEFile = data.IsPEFile;
            ImageBase = data.ILImageBase;
            IsFileLayout = data.IsFileLayout;
            Size = data.Size;
            MetadataAddress = data.MetadataStart;
            MetadataLength = data.MetadataLength;
            IsDynamic = data.IsReflection || string.IsNullOrWhiteSpace(Name);
        }

        public ClrmdModule(ClrAppDomain parent, IModuleHelpers helpers, ulong addr)
        {
            AppDomain = parent;
            _helpers = helpers;
            Address = addr;
        }

        public override PdbInfo? Pdb
        {
            get
            {
                if (_pdb is null)
                {
                    using ReadVirtualStream stream = new ReadVirtualStream(_helpers.DataReader, (long)ImageBase, (long)(Size > 0 ? Size : 0x1000));
                    using PEImage pefile = new PEImage(stream, !IsFileLayout);
                    if (pefile.IsValid)
                        _pdb = pefile.DefaultPdb;
                }

                return _pdb;
            }
        }

        public override DebuggableAttribute.DebuggingModes DebuggingMode
        {
            get
            {
                if (_debugMode == int.MaxValue)
                    _debugMode = GetDebugAttribute();

                DebugOnly.Assert(_debugMode != int.MaxValue);
                return (DebuggableAttribute.DebuggingModes)_debugMode;
            }
        }

        private unsafe int GetDebugAttribute()
        {
            MetadataImport? metadata = MetadataImport;
            if (metadata != null)
            {
                try
                {
                    if (metadata.GetCustomAttributeByName(0x20000001, "System.Diagnostics.DebuggableAttribute", out IntPtr data, out uint cbData) && cbData >= 4)
                    {
                        byte* b = (byte*)data.ToPointer();
                        ushort opt = b[2];
                        ushort dbg = b[3];

                        return (dbg << 8) | opt;
                    }
                }
                catch (SEHException)
                {
                }
            }

            return (int)DebuggableAttribute.DebuggingModes.None;
        }

        public override IEnumerable<(ulong, uint)> EnumerateTypeDefToMethodTableMap() => _helpers.GetSortedTypeDefMap(this);

        public override ClrType? GetTypeByName(string name)
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            if (name.Length == 0)
                throw new ArgumentException($"{nameof(name)} cannot be empty");

            IReadOnlyList<(ulong, uint)> typeDefMap = _helpers.GetSortedTypeDefMap(this);

            List<ulong> lookup = new List<ulong>(Math.Min(256, typeDefMap.Count));

            foreach ((ulong mt, uint _) in EnumerateTypeDefToMethodTableMap())
            {
                ClrType type = _helpers.TryGetType(mt);
                if (type is null)
                {
                    lookup.Add(mt);
                }
                else if (type.Name == name)
                {
                    return type;
                }
            }

            foreach (ulong mt in lookup)
            {
                string? typeName = _helpers.GetTypeName(mt);
                if (typeName == name)
                    return _helpers.Factory.GetOrCreateType(mt, 0);
            }

            return null;
        }

        public override ClrType? ResolveToken(uint typeDefOrRefToken)
        {
            ClrHeap? heap = AppDomain?.Runtime?.Heap;
            if (heap is null)
                return null;

            IReadOnlyList<(ulong, uint)> map;
            if ((typeDefOrRefToken & 0x02000000) != 0)
                map = _typeDefMap ??= _helpers.GetSortedTypeDefMap(this);
            else if ((typeDefOrRefToken & 0x01000000) != 0)
                map = _typeRefMap ??= _helpers.GetSortedTypeRefMap(this);
            else
                throw new NotSupportedException($"ResolveToken does not support this token type: {typeDefOrRefToken:x}");

            int index = map.Search(typeDefOrRefToken & ~0xff000000, CompareTo);
            if (index == -1)
                return null;

            return _helpers.Factory.GetOrCreateType(map[index].Item1, 0);
        }

        private static int CompareTo((ulong, uint) entry, uint token) => entry.Item2.CompareTo(token);
    }
}