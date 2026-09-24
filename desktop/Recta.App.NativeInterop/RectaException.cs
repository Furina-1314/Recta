namespace Recta.App.NativeInterop;

// C ABI 负错误码的托管常量(与 recta_capi.h 严格一致)。
public static class RectaErrors
{
    public const int InvalidArg = -1;
    public const int Permission = -2;
    public const int Auth = -3;
    public const int State = -4;
    public const int WeakPassword = -5;
    public const int Db = -6;
    public const int Logic = -7;
    public const int NotReady = -8;
    public const int BufferSmall = -9;
    public const int Unknown = -10;
}

// 原生层负错误码的托管面容。Code 与 recta_capi.h 一致;Message 来自 recta_last_error。
public sealed class RectaException : Exception
{
    public int Code { get; }

    public RectaException(int code, string message) : base(message) => Code = code;

    public bool IsPermission => Code == RectaErrors.Permission;
    public bool IsAuth => Code == RectaErrors.Auth;
    public bool IsState => Code == RectaErrors.State;
    public bool IsInvalidArgument => Code == RectaErrors.InvalidArg;
    public bool IsDatabase => Code == RectaErrors.Db;
}
