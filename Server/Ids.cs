using System;
using System.Security.Cryptography;
using System.Text;

namespace VisitAPI.Server;

static class Ids
{
    /// <summary>由文字派生的 24 位十六进制（MongoId 格式）：UTF-8 的 MD5 前 12 字节，小写。同样的文字永远得到同一个。
    /// 算法不能改：护卫变量的 id（EscortVars.Id）已经存进玩家档案，还得和客户端 DialogTemplateBuilder.Id 算出来的对上；DialogueSanitizer 拿它派生随机变量名。</summary>
    public static string Hex24(string text) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)), 0, 12);
}
