using System.Windows.Input;
using CH_V01.App.Commands;

using CH_V01.App.Support;

namespace CH_V01.App.ViewModels;

public sealed class HelpViewModel : DialogViewModelBase
{
    public HelpViewModel(string productName, string mesPlugin)
    {
        ProductName = productName;
        MesPlugin = string.IsNullOrWhiteSpace(mesPlugin) || string.Equals(mesPlugin, "None", StringComparison.OrdinalIgnoreCase)
            ? "未配置"
            : "已配置";
        CloseCommand = new RelayCommand(() => Close(true));
    }

    public string ProductName { get; }

    public string MesPlugin { get; }

    public string ProductTitle => "CeHao";

    public string Version => $"当前版本：{ApplicationVersion.Current}";

    public string VersionHistory =>
        "CH_V02.27：新增奕东 EAP（TIMS 后段接口）MES 插件与测试结果导出器；支持扫码前过站校验、测试后报工、可选治具/模具累计次数上报，以及原始 CSV、HYTM TXT 或两种格式同时导出。"
        + "\n\n"
        + "CH_V02.23：消息提醒支持居中放大与红色警示配色；自定义 CSV 导出器将摘要字段与测试项目表体合并为同一张横向表。"
        + "\n\n"
        + "CH_V02.22：产品 CSV 保存路径支持按测试结果创建 PASS/FAIL 文件夹，并将 CSV 写入对应目录。"
        + "\n\nCH_V02.21：支持通过 appsettings.json 配置默认操作员自动登录；操作员仅保留保存路径和通讯配置入口。"
        + "\n\nCH_V02.18：基础配置新增启动自动连接扫码枪、下位机开关；启用后程序启动时自动连接对应设备，连接失败会弹出消息提醒并写入运行日志。"
        + "\n\nCH_V02.17：测试结果不再弹出消息提醒；MES 上传失败或超时改为淡黄色醒目提醒。消息提醒支持队列显示，MES 上传失败不阻塞测试流程；管理员工具新增模拟消息提醒按钮，点击消息框即可关闭。"
        + "\n\nCH_V02.16：奕东 EAP 测试结果导出器新增 HYTM Statistic TXT 格式，可与原始 CSV 同时导出；支持配置测试文件路径、板号、设备号、操作员和 Block 信息。"
        + "\n\nCH_V02.15：MES 调试工具新增多行请求头输入框，支持 Authorization、Token、X-Api-Key 等自定义请求头；每行按“名称: 值”填写。"
        + "\n\nCH_V02.14：新增奕东 EAP（TIMS 后段接口）MES 插件与原始测试 CSV 导出插件。MES 插件支持扫码前 SN/工艺路线校验、测试后 PASS/FAIL 过站报工，以及可选治具/模具累计次数上报；插件配置可填写 LOT、设备、工位、治具、扣料等字段。"
        + "\n\nCH_V02.13：发布当前 MES 卡关诊断和站位取值修正版本。"
        + "\n\nCH_V02.12：MES 卡关和上传统一使用 MES 配置页“站位”输入框的值；旧 Mes.StationNo 配置不再覆盖界面设置。"
        + "\n\nCH_V02.11：PBHMes 卡关日志新增请求 URL、请求体、Token 携带状态、HTTP 响应状态及返回内容，便于现场排查 checkRoute 接口异常。"
        + "\n\nCH_V02.10：扫码模式改为自动扫码、手动扫码、不扫码三选一；手动扫码可启用扫码先行，扫码校验成功后记录 SCANOK，待下位机发送 DM 后再回复 DMOK 并开始测试。"
        + "\n\nCH_V02.09：主界面 MES 切换在线时仅 Ping 配置 MES 地址的主机；主机可达即可切换在线，不再执行 MES 登录校验。"
        + "\n\nCH_V02：本版本保持兼容上个版本，在原有融合框架基础上补充项目方案、MES 通讯插件和结果导出插件能力，使系统可以适配更多现场接口和文件格式。"
        + "\n\n同时新增 MES 调试工具页面，支持在软件内手动选择 GET/POST 接口、填写请求参数或请求体并查看返回结果，方便现场联调、接口验证和异常排查。"
        + "\n\n界面 UI 与布局也进行了更新，主界面、配置入口和功能分组更清晰，常用操作更容易定位。"
        + "\n\n配置项按实际职责重新区分和优化：扫码规则、产品识别和限值联动等放入基础设置；下位机握手命令归入通讯设置；MES 参数继续归入 MES/插件配置，使维护边界更明确，后续项目方案扩展也更方便。";

    public ICommand CloseCommand { get; }
}
