using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CDUniTap.Interfaces;
using CDUniTap.Models.Options;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace CDUniTap.Services.Api;

public partial class CasServiceApi : IHttpApiServiceBase
{
    private readonly HttpClient _httpClient;
    private readonly CasServiceApiOptions _options;

    private static readonly RSACryptoServiceProvider RsaProvider = CreateRsaProviderFromPublicKey(
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwU7Wty6I3Sr4Z6onpSZRU39XNGdHYPIKFf8T7UP2FqihiOJEnFIF0n6tcncjDYGEnalhFiq/n8dXkhUCQWMPv2C1pT1PxT25SBVZlZNXq+abudxNoOGzc5kPdVuDIq4Nq7RfZHrbeu7IaWmxEmDm9zb/Q+VI9EIFM92p3e0ZLLfAwDASXzod9x4ocmBFXGuaDVGA8cPQxNvNXgKit5oLWMa4B1YZ0IMDSZbqpaM2llgQ0anN5VQYwFHSOMZy2LCYq97Db33rC74AbHWw7/bmO15p4q2y4t7qCbmaRIhGlpicCeETl/pljqksZ95/ckYW7Q5H/nSJT75ImYF0jEgIrQIDAQAB");

    public CasServiceApi(HttpClient httpClient, CasServiceApiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.DefaultRequestHeaders.Referrer = new Uri("https://cas.paas.cdut.edu.cn/cas/login");
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                                                                  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36 Edg/116.0.1938.69");
    }


    public async Task<bool> LoginWithPasswordAsync(string username, string password)
    {
        _httpClient.DefaultRequestHeaders.Referrer =
            new Uri($"https://cas.paas.cdut.edu.cn/cas/login");
        var loginPage = await _httpClient.GetStringAsync($"https://cas.paas.cdut.edu.cn/cas/login");
        var execution = Regex.Match(loginPage, "execution\\\" value=\\\"(e[1-9]*s[1-9]*)\\\"").Groups[1].Value;
        var encryptedPassword = EncryptPassword(password);
        var response = await _httpClient.PostAsync($"https://cas.paas.cdut.edu.cn/cas/login", new FormUrlEncodedContent(
                                                       new Dictionary<string, string>()
                                                       {
                                                           { "username", username },
                                                           { "password", encryptedPassword },
                                                           { "captcha", "" },
                                                           { "rememberMe", "true" },
                                                           { "currentMenu", "1" },
                                                           { "failN", "0" },
                                                           { "mfaState", "" },
                                                           { "execution", execution },
                                                           { "_eventId", "submit" },
                                                           { "geolocation", "" },
                                                           { "submit", "Login1" },
                                                       }));
#if DEBUG
        var result = await response.Content.ReadAsStringAsync();
#endif

        if (!response.IsSuccessStatusCode) return response.IsSuccessStatusCode;
        if (!result.Contains("successRedirectUrl")) return false;
        _options.Password = encryptedPassword;
        _options.Username = username;
        _options.StudentId = LoginSuccessfulStudentIdRegex().Match(result).Groups[1].Value;
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> LoginWithSmsCodeAsync(string phone, string smsCode)
    {
        _httpClient.DefaultRequestHeaders.Referrer =
            new Uri($"https://cas.paas.cdut.edu.cn/cas/login");
        var loginPage = await _httpClient.GetStringAsync($"https://cas.paas.cdut.edu.cn/cas/login");
        var execution = Regex.Match(loginPage, "execution\\\" value=\\\"(e[1-9]*s[1-9]*)\\\"").Groups[1].Value;
        var response = await _httpClient.PostAsync($"https://cas.paas.cdut.edu.cn/cas/login", new FormUrlEncodedContent(
                                                       new Dictionary<string, string>()
                                                       {
                                                           { "username", phone },
                                                           { "password", smsCode },
                                                           { "currentMenu", "2" },
                                                           { "failN", "-1" },
                                                           { "execution", execution },
                                                           { "_eventId", "submitPasswordlessToken" },
                                                           { "geolocation", "" },
                                                           { "submit", "Login2" },
                                                       }));
#if DEBUG
        var result = await response.Content.ReadAsStringAsync();
#endif

        if (!response.IsSuccessStatusCode) return response.IsSuccessStatusCode;
        if (!result.Contains("successRedirectUrl")) return false;
        _options.StudentId = LoginSuccessfulStudentIdRegex().Match(result).Groups[1].Value;
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SendSmsCode(string phone)
    {
        var response = await _httpClient.PostAsync("https://cas.paas.cdut.edu.cn/cas/passwordlessTokenSend",
                                                   new FormUrlEncodedContent(new Dictionary<string, string>()
                                                                             {
                                                                                 { "username", phone }
                                                                             }));
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> AuthenticateService(string serviceUrl)
    {
        var response = await _httpClient.GetAsync($"https://cas.paas.cdut.edu.cn/cas/login?service={WebUtility.HtmlEncode(serviceUrl)}");
        return response.Headers.Location?.ToString();
    }
    
    
    private string EncryptPassword(string password)
    {
        if (password.StartsWith("__RSA__")) return password;
        var rsaPassword = Convert.ToBase64String(RsaEncrypt(Encoding.UTF8.GetBytes(password)));
        return "__RSA__" + rsaPassword;
    }

    private static byte[] RsaEncrypt(byte[] buffer)
    {
        return RsaProvider.Encrypt(buffer, false);
    }

    private static RSACryptoServiceProvider CreateRsaProviderFromPublicKey(string publicKeyString)
    {
        // encoded OID sequence for  PKCS #1 rsaEncryption szOID_RSA_RSA = "1.2.840.113549.1.1.1"
        byte[] SeqOID = { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 };
        byte[] x509key;
        byte[] seq = new byte[15];
        int x509size;

        x509key = Convert.FromBase64String(publicKeyString);
        x509size = x509key.Length;

        // ---------  Set up stream to read the asn.1 encoded SubjectPublicKeyInfo blob  ------
        using (MemoryStream mem = new MemoryStream(x509key))
        {
            using (BinaryReader binr = new BinaryReader(mem)) //wrap Memory Stream with BinaryReader for easy reading
            {
                byte bt = 0;
                ushort twobytes = 0;

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130) //data read as little endian order (actual data order for Sequence is 30 81)
                {
                    binr.ReadByte(); //advance 1 byte
                }
                else if (twobytes == 0x8230)
                {
                    binr.ReadInt16(); //advance 2 bytes
                }
                else
                {
                    return null;
                }

                bool CompareBytearrays(byte[] a, byte[] b)
                {
                    if (a.Length != b.Length)
                    {
                        return false;
                    }

                    int i = 0;
                    foreach (byte c in a)
                    {
                        if (c != b[i])
                        {
                            return false;
                        }

                        i++;
                    }

                    return true;
                }


                seq = binr.ReadBytes(15);            //read the Sequence OID
                if (!CompareBytearrays(seq, SeqOID)) //make sure Sequence for OID is correct
                {
                    return null;
                }

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8103) //data read as little endian order (actual data order for Bit String is 03 81)
                {
                    binr.ReadByte(); //advance 1 byte
                }
                else if (twobytes == 0x8203)
                {
                    binr.ReadInt16(); //advance 2 bytes
                }
                else
                {
                    return null;
                }

                bt = binr.ReadByte();
                if (bt != 0x00) //expect null byte next
                {
                    return null;
                }

                twobytes = binr.ReadUInt16();
                if (twobytes == 0x8130) //data read as little endian order (actual data order for Sequence is 30 81)
                {
                    binr.ReadByte(); //advance 1 byte
                }
                else if (twobytes == 0x8230)
                {
                    binr.ReadInt16(); //advance 2 bytes
                }
                else
                {
                    return null;
                }

                twobytes = binr.ReadUInt16();
                byte lowbyte = 0x00;
                byte highbyte = 0x00;

                if (twobytes == 0x8102) //data read as little endian order (actual data order for Integer is 02 81)
                {
                    lowbyte = binr.ReadByte(); // read next bytes which is bytes in modulus
                }
                else if (twobytes == 0x8202)
                {
                    highbyte = binr.ReadByte(); //advance 2 bytes
                    lowbyte = binr.ReadByte();
                }
                else
                {
                    return null;
                }

                byte[] modint =
                    { lowbyte, highbyte, 0x00, 0x00 }; //reverse byte order since asn.1 key uses big endian order
                int modsize = BitConverter.ToInt32(modint, 0);

                int firstbyte = binr.PeekChar();
                if (firstbyte == 0x00)
                {
                    //if first byte (highest order) of modulus is zero, don't include it
                    binr.ReadByte(); //skip this null byte
                    modsize -= 1;    //reduce modulus buffer size by 1
                }

                byte[] modulus = binr.ReadBytes(modsize); //read the modulus bytes

                if (binr.ReadByte() != 0x02) //expect an Integer for the exponent data
                {
                    return null;
                }

                int expbytes =
                    binr.ReadByte(); // should only need one byte for actual exponent data (for all useful values)
                byte[] exponent = binr.ReadBytes(expbytes);

                // ------- create RSACryptoServiceProvider instance and initialize with public key -----
                RSACryptoServiceProvider RSA = new RSACryptoServiceProvider();
                RSAParameters RSAKeyInfo = new RSAParameters();
                RSAKeyInfo.Modulus = modulus;
                RSAKeyInfo.Exponent = exponent;
                RSA.ImportParameters(RSAKeyInfo);

                return RSA;
            }
        }
    }

    [GeneratedRegex("<strong><span>(.*)<\\/span>")]
    private static partial Regex LoginSuccessfulStudentIdRegex();
}