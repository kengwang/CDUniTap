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
        var encryptedPassword = await EncryptPassword(password);
        _httpClient.DefaultRequestHeaders.Referrer =
            new Uri($"https://cas.paas.cdut.edu.cn/cas/login");
        var loginPage = await _httpClient.GetStringAsync($"https://cas.paas.cdut.edu.cn/cas/login");
        var execution = Regex.Match(loginPage, "execution\\\" value=\\\"(.*?)\\\"").Groups[1].Value;
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
                { "submit1", "Login1" },
            }));

        var result = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return response.IsSuccessStatusCode;
        if (!result.Contains("successRedirectUrl")) return false;
        _options.Password = encryptedPassword;
        _options.Username = username;
        _options.StudentId = LoginSuccessfulStudentIdRegex().Match(result).Groups[1].Value;
        return response.IsSuccessStatusCode;
    }

    public async Task<string> EncryptPassword(string password)
    {
        if (password.StartsWith("__RSA__"))
            return password;
        var rsaKeyResp = await _httpClient.GetStringAsync("https://cas.paas.cdut.edu.cn/cas/jwt/publicKey");
        var rsa = new JSEncryptRSA(rsaKeyResp);
        var encryptedPassword = $"__RSA__{rsa.Encrypt(password)}";
        return encryptedPassword;
    }
    
    public async Task<bool> LoginWithSmsCodeAsync(string phone, string smsCode)
    {
        _httpClient.DefaultRequestHeaders.Referrer =
            new Uri($"https://cas.paas.cdut.edu.cn/cas/login");
        var loginPage = await _httpClient.GetStringAsync($"https://cas.paas.cdut.edu.cn/cas/login");
        var execution = Regex.Match(loginPage, "execution\\\" value=\\\"(.*?)\\\"").Groups[1].Value;
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

        var result = await response.Content.ReadAsStringAsync();
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
        var response =
            await _httpClient.GetAsync(
                $"https://cas.paas.cdut.edu.cn/cas/login?service={WebUtility.HtmlEncode(serviceUrl)}");
        return response.Headers.Location?.ToString();
    }


    public class JSEncryptRSA
    {
        private readonly RSA _rsa;

        public JSEncryptRSA(string publicKeyPem)
        {
            _rsa = RSA.Create();
            _rsa.ImportFromPem(publicKeyPem.ToCharArray());
        }


        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return null;

            var data = Encoding.UTF8.GetBytes(plainText);
            var encrypted = _rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
            return Convert.ToBase64String(encrypted);
        }
    }


    [GeneratedRegex("<strong><span>(.*)<\\/span>")]
    private static partial Regex LoginSuccessfulStudentIdRegex();
}