using System.Text.Json;
using CDUniTap.Interfaces.Markers;
using CDUniTap.Models.Options;
using CDUniTap.Services.Api;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace CDUniTap.Cli;

public class CasCliCommander : ICliCommander
{
    private CasServiceApi _casService;
    private readonly CasServiceApiOptions _casOptions;

    public CasCliCommander(CasServiceApi casService, CasServiceApiOptions casOptions)
    {
        _casService = casService;
        _casOptions = casOptions;
    }

    public async Task EnterCommander()
    {
        var isLogined = false;
        var firstTry = true;
        while (!isLogined)
        {
            string? loginMode = null;
            if (_casOptions.StudentId is not null && firstTry)
                loginMode = "自动登录";
            if (loginMode is null)
            {
                // 用户未登录, 请求登录
                AnsiConsole.MarkupLine(
                    "[red]当前用户未登录, 请使用 成都理工大学统一身份认证系统 (https://cas.paas.cdut.edu.cn/cas/login) 账号 进行登录[/]");
                loginMode = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                               .Title("请选择你的登录方式: ")
                                               .AddChoices("账号密码登录", "手机验证码登录"));
            }

            switch (loginMode)
            {
                case "手机验证码登录":
                    var phone = AnsiConsole.Ask<string>("请输入手机号:");
                    var smsResult = false;
                    await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("正在发送短信验证码",
                        async _ => { smsResult = await _casService.SendSmsCode(phone); });
                    if (!smsResult)
                    {
                        AnsiConsole.MarkupLine("[red]验证码发送失败, 请重试[/]");
                        continue;
                    }

                    AnsiConsole.MarkupLine("已尝试将短信发送到手机, 请注意查收");
                    var smsCode = AnsiConsole.Ask<string>("请输入短信验证码: ");
                    await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(
                        "正在登录到 成都理工大学统一身份认证系统",
                        async _ => { isLogined = await _casService.LoginWithSmsCodeAsync(phone, smsCode); });
                    break;
                default:
                    var username = _casOptions.Username;
                    var password = _casOptions.Password;
                    if (loginMode != "自动登录")
                    {
                        username = AnsiConsole.Ask<string>("请输入用户名:");
                        password = AnsiConsole.Prompt(new TextPrompt<string>("请输入你的密码:").Secret());
                    }

                    loginMode = "账号密码登录";
                    await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(
                        "正在登录到 成都理工大学统一身份认证系统",
                        async _ => { isLogined = await _casService.LoginWithPasswordAsync(username, password); });
                    if (isLogined)
                        await File.WriteAllTextAsync("config.json", JsonSerializer.Serialize(_casOptions));
                    break;
            }

            if (!isLogined)
            {
                firstTry = false;
                AnsiConsole.MarkupLine("[red]用户登录失败, 请检查登录信息是否正确![/]");
            }
        }


        AnsiConsole.MarkupLine($"[green]登录成功, 欢迎 {_casOptions.StudentId}[/]");
    }
}