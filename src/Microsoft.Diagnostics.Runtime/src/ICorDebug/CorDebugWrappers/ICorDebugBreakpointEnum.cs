using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.ICorDebug
{
    [ComImport, Guid("CC7BCB03-8A68-11D2-983C-0000F808342D"), ComConversionLoss, InterfaceType(1)]
    public interface ICorDebugBreakpointEnum : ICorDebugEnum
    {

        new void Skip([In] uint celt);

        new void Reset();

        new void Clone([Out, MarshalAs(UnmanagedType.Interface)] out ICorDebugEnum ppEnum);

        new void GetCount([Out] out uint pcelt);
        [PreserveSig, MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
        int Next([In] uint celt, [Out, MarshalAs(UnmanagedType.LPArray)] ICorDebugBreakpoint[] breakpoints, [Out] out uint pceltFetched);
    }
}