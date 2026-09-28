using System.Windows.Input;
using CH_V03.App.Commands;

using CH_V03.App.Support;

namespace CH_V03.App.ViewModels;

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
        "CH_V02.87：华勤账号首次登录后自动记住本机登录状态；切换登录时免输入账号密码。手动扫码切换为自动扫码时，扫码枪通讯方式自动恢复为串口并重连。"
        + "\n\nCH_V02.85：HongXin 主界面移除批次号，生产流程单号作为流程单开工依据；首次填写或切换流程单号时开工，同一流程单号不重复开工。"
        + "\n\nCH_V02.84：HongXin MES 的开工、产能和过站流程单号统一取主界面生产流程单号，不再误用工单号。"
        + "\n\nCH_V02.83：接收 Item1 表头首段后立即暂停 Itemnum 重试，等待完整 end 再确认表头，避免长表头分包时重复请求。"
        + "\n\nCH_V02.82：选择 HongXin 插件时主界面新增生产流程单号、批次号；流程单开工仅在批次号首次输入或切换后调用。"
        + "\n\nCH_V02.81：HongXin OA ICT 上传的测试字段统一传整数 0；非数字设备代号不再发送给 int 类型的 Index 字段，实测值保留给新 MES。"
        + "\n\nCH_V02.80：MES 上传增加 CSV 附件诊断日志；导出器未回传路径时，自动从当前测试时间段的 CSV 输出目录回退查找附件。"
        + "\n\nCH_V02.79：HongXin 新 MES 请求按最新版示例使用横杠时间格式、整数字段，并补齐特性上下限和单位字段。"
        + "\n\nCH_V02.78：HongXin 插件配置按原 OA、公共 MES、设备产能上传、设备单品过站和分类映射分组显示。"
        + "\n\nCH_V02.77：HongXin MES 按 FCT/ICT 测试分类汇总大类特性代号，并将同类测试项明细以 JSON 写入 sp_require。"
        + "\n\nCH_V02.76：HongXin 保留原 OA 上传，并新增 MES 流程单开工、设备产能上传及设备单品过站；可在插件配置中选择 MES 上传模式并上传本次 CSV。"
        + "\n\nCH_V02.75：HongXin OA 支持解析 ASMX XML 外层响应；上传失败时，接口 MSG 内容会显示在失败提醒框中。"
        + "\n\nCH_V02.74：HongXin OA 上传改为与现场 Postman 一致的 x-www-form-urlencoded 表单格式，使用 JSON 字段提交请求内容。"
        + "\n\nCH_V02.73：HongXin OA 上传失败时，运行日志输出请求地址、发送 JSON、HTTP 响应状态及响应内容，便于现场排查。"
        + "\n\nCH_V02.72：HongXin OA MES 插件程序集重命名为 CH_V03.Mes.HongXin.dll。"
        + "\n\nCH_V02.71：启动时自动创建或修复 huaqin 管理员账号，默认密码为 admin。"
        + "\n\nCH_V02.70：奕东 EAP 导出 CSV 中，+% 列写入测试项目上限，-% 列写入测试项目下限。"
        + "\n\nCH_V02.69：昆山嘉联益基础资料页面隐藏允许连续重复测试选项，保留当前已保存的重复测试逻辑，不提供现场修改入口。"
        + "\n\nCH_V02.68：昆山嘉联益基础资料新增品号输入，并同步给 MES 与导出器；CSV 文件优先按品号加年月日命名。"
        + "\n\nCH_V02.67：昆山嘉联益基础资料页面中的扫码枪、下位机和 MES 状态按钮改为不可用，仅保留连接状态显示。"
        + "\n\nCH_V02.66：新增 HongXin OA MES 上传插件，支持 ICT 的 SNICTUpload 与 FCT 的 FunctionalTestUpload 接口；测试项目按协议字段顺序上传。"
        + "\n\nCH_V02.65：新增 huaqin 管理员账号；首次手动登录成功后，本机后续启动自动以该账号的管理员权限进入系统。"
        + "\n\nCH_V02.64：切换手动扫码时，扫码枪通讯方式自动切换为 USB POS HID；基础资料新增允许连续重复测试开关，未启用时仅拦截与上一笔实际开始测试相同的条码。"
        + "\n\nCH_V02.60：奕东 EAP 竖版 CSV 测试明细按导出器配置的 CompTest、FunTest 序号分别输出；OpenTest、ShortTest 仅保留失败点位汇总，不写入测试明细表。"
        + "\n\nCH_V02.59：奕东 EAP 竖版 CSV 去除失败点位显示的撇号；CompTest 中多点 SHORT 明细按组展开为多行，保留项目名和限值。"
        + "\n\nCH_V02.58：昆山嘉联益将治具 Tooling 恢复到 MES 与导出插件的上方共用资料区；基础资料中新增条码固定校验码，留空时不校验。"
        + "\n\nCH_V02.57：奕东 EAP 竖版 CSV 的 Open/Short 失败点位按每组 高点 - 低点 单独一行导出，Count 自动统计点位对数量，并强制 Excel 文本格式。"
        + "\n\nCH_V02.56：昆山嘉联益导出设置新增独立可编辑的治具 Tooling；MES 返回内容去除全部空白并统一冒号后，包含 MES卡关通过:標準片 或简体标准片时自动切换标准片模式。"
        + "\n\nCH_V02.55：奕东 EAP 竖版 CSV 的多点 SHORT NG 明细按高低点成对导出，并使用 UTF-8 BOM，Excel 打开中文不再乱码。"
        + "\n\nCH_V02.54：下位机 SHORT NG 多点明细拆分显示为点位 A、点位 B 和测试值三列；原有带点位与紧凑结果报文继续兼容。"
        + "\n\nCH_V02.53：自定义 CSV 导出器参考客户示例改为横向宽表输出：摘要字段在左、测试项目横向展开，并按需输出点位、上下限、单位和测试结果行。"
        + "\n\nCH_V02.52：下位机 SHORT NG 测试结果支持以 ^ 分隔多组 高点-低点-阻值 明细，并在同一测试项行显示；单项末尾的 ^ 自动忽略。"
        + "\n\nCH_V02.51：奕东 EAP 导出器仅按四类测试序号配置归类；未配置序号的 CompTest、FunTest 默认输出 Pass。"
        + "\n\nCH_V02.50：奕东 EAP 导出器不再输出 CompFailCount、FunFailCount；仅保留 CompTest、FunTest 的 Pass/Fail 结果。"
        + "\n\nCH_V02.49：奕东 EAP 导出器不再输出 CompTest、FunTest 的 NG 项目和点位；OpenTest、ShortTest 的 Count 与失败项之间保留空行。"
        + "\n\nCH_V02.48：奕东 EAP 导出器的 HYTM TXT 可单独配置保存路径，竖版 CSV 与 TXT 不再必须归档到同一目录。"
        + "\n\nCH_V02.47：奕东 EAP 导出器可按配置的测试序号将项目归类为 OpenTest、ShortTest、CompTest、FunTest，并据此输出各类结果和 NG 明细。"
        + "\n\nCH_V02.46：奕东 EAP 导出器的各类 NG 明细同时输出测试项目名称和对应点位。"
        + "\n\nCH_V02.45：昆山嘉联益专属模式的标准片模式跳过测试前 MES 卡关；生产模式保留卡关，调试模式继续跳过卡关和上传。"
        + "\n\nCH_V02.44：下位机 reset 后收到 DM 时，先立即请求并确认 Item1 测试项表头，再发送 DMOK 或启动扫码流程，避免复位后的第一轮测试结果被忽略。"
        + "\n\nCH_V02.42：奕东 EAP 的 OpenTest、ShortTest、CompTest、FunTest 固定输出；未测试的类型默认 Pass，测试后按实际结果输出；NG 同步写入具体类型、数量和点位。"
        + "\n\nCH_V02.41：昆山嘉联益专属模式新增 CSV 档案占用预检，并将测试片模式更名为生产模式，主界面增加模式状态区。"
        + "\n\nCH_V02.40：奕东 EAP 竖版 CSV 按 Open、Short、Comp、Fun 分类输出具体 NG 类型、失败数量和对应点位；FunTest 支持固定结果及按需写入明细。"
        + "\n\nCH_V02.39：奕东 EAP 在不扫码模式下跳过 SN 过站验证和过站报工，仅上传治具累计使用信息；带条码时继续执行原有过站流程。"
        + "\n\nCH_V02.37：昆山嘉联益 MES 上传不再传递中文内部状态；急停后下一轮结果可使用 DM 时间兜底计算测试时长。"
        + "\n\nCH_V02.36：移除扫码重复码拦截，后续扫码不再因与上一条码相同或存在包含关系而被忽略。"
        + "\n\nCH_V02.35：文件夹选择按钮统一为浅蓝描边的紧凑浏览样式，并补齐悬停、按下和禁用状态。"
        + "\n\nCH_V02.34：奕东 EAP 导出器支持横版和竖版 CSV 独立勾选导出，并可分别配置保存路径；横版格式与嘉联益导出器一致。"
        + "\n\nCH_V02.33：普通二级窗口禁止缩放；基础配置和插件配置中的保存目录支持通过文件夹选择器填写。"
        + "\n\nCH_V02.32：新增昆山嘉联益专用模式；锁定嘉联益 MES/导出插件，集中配置资料、通讯和运行模式，并支持调试 CSV 后缀。"
        + "\n\nCH_V02.31：急停复位或重新连接后检查 Item1 表头；表头缺失时自动发送 Itemnum 请求，收到表头即停止，最多请求 3 次。"
        + "\n\n"
        + "CH_V02.30：修复急停或下位机报文超时后第二次正常测试结果未保存的问题；旧周期残留数据丢弃至下一次 DM，新周期不再强制等待 Item1 表头。"
        + "\n\n"
        + "CH_V02.29：扫码枪通讯新增 USB POS HID；支持 Windows POS HID 设备连接与扫码数据接收，手动扫码兼容键盘式条码输入，并阻止扫码尾部 Enter 误触发连接/断开按钮。"
        + "\n\n"
        + "CH_V02.28：测试项目表格列顺序固定但仍可调整列宽；奕东 CSV 改为按天汇总；自动扫码新增默认 10 秒超时，超时后发送 DMNG。"
        + "\n\n"
        + "CH_V02.27：新增奕东 EAP（TIMS 后段接口）MES 插件与测试结果导出器；支持扫码前过站校验、测试后报工、可选治具/模具累计次数上报，以及原始 CSV、HYTM TXT 或两种格式同时导出。"
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
