using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Util;
using static XIVLauncher.Common.Game.Launcher;

namespace XIVLauncher.Common.Game.Exceptions;


//-10242296 该账号首次在本设备上登录，不支持一键登录，请使用二维码、动态密码或密码登录
//-10386010 请您使用叨鱼APP扫码或一键方式登录

///authen/staticLogin.json
//-10386188 登录环境存在风险，请使用叨鱼扫码登录，或通过安全手机发送短信LSFE02至1068290184266移动\/1065502184079联通\/1065902100920电信后再继续操作。
//-10242226 动态密码错误（静态密码已锁定，请在【叨鱼】中设置静密锁）

///authen/cancelPushMessageLogin.json
//-10242301 您的输入有误，请确认后重新输入

///authen/codeKeyLogin.json
//-10515805 二维码未通过验证，请重试

public enum SdoLoginCustomExpectionCode
{ 
    SLIDE_TIMEOUT_OR_CANCELED = 11451400,
    STATIC_NEED_CAPTCHA,
    SCAN_QRCODE_GET_ACCOUNT_FAIL,
    SCAN_TIMEOUT_OR_CANCELED
}

[Serializable]
public class SdoLoginException : Exception
{
    public int ErrorCode;
    //public string OauthErrorResult { get; private set; }
    public bool RemoveAutoLoginSessionKey;

    public SdoLoginException(int errorCode,string message,bool removeAutoLoginSessionKey=false)
        : base(message)
    {
        this.ErrorCode = errorCode;
        this.RemoveAutoLoginSessionKey = removeAutoLoginSessionKey;
    }
}
