$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$imageDir = Join-Path $root "images"
$docxPath = Join-Path $root "测昊-工业自动化测试工作站软件使用文档CH_V02.docx"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function XmlEscape($text) {
    return [System.Security.SecurityElement]::Escape([string]$text)
}

function ParagraphXml($text, $style = $null) {
    $styleXml = if ($style) { "<w:pPr><w:pStyle w:val=`"$style`"/></w:pPr>" } else { "" }
    return "<w:p>$styleXml<w:r><w:t xml:space=`"preserve`">$(XmlEscape $text)</w:t></w:r></w:p>"
}

function BulletXml($text) {
    return "<w:p><w:pPr><w:numPr><w:ilvl w:val=`"0`"/><w:numId w:val=`"1`"/></w:numPr></w:pPr><w:r><w:t xml:space=`"preserve`">$(XmlEscape $text)</w:t></w:r></w:p>"
}

function PageBreakXml() {
    return "<w:p><w:r><w:br w:type=`"page`"/></w:r></w:p>"
}

function TableXml($headers, $rows) {
    $xml = New-Object System.Collections.Generic.List[string]
    $xml.Add("<w:tbl><w:tblPr><w:tblStyle w:val=`"TableGrid`"/><w:tblW w:w=`"0`" w:type=`"auto`"/><w:tblBorders><w:top w:val=`"single`" w:sz=`"6`" w:color=`"9CCFEF`"/><w:left w:val=`"single`" w:sz=`"6`" w:color=`"9CCFEF`"/><w:bottom w:val=`"single`" w:sz=`"6`" w:color=`"9CCFEF`"/><w:right w:val=`"single`" w:sz=`"6`" w:color=`"9CCFEF`"/><w:insideH w:val=`"single`" w:sz=`"4`" w:color=`"CFE8F8`"/><w:insideV w:val=`"single`" w:sz=`"4`" w:color=`"CFE8F8`"/></w:tblBorders></w:tblPr>")
    $xml.Add("<w:tr>")
    foreach ($header in $headers) {
        $xml.Add("<w:tc><w:tcPr><w:shd w:fill=`"DFF3FF`"/></w:tcPr><w:p><w:r><w:rPr><w:b/></w:rPr><w:t xml:space=`"preserve`">$(XmlEscape $header)</w:t></w:r></w:p></w:tc>")
    }
    $xml.Add("</w:tr>")
    foreach ($row in $rows) {
        $xml.Add("<w:tr>")
        foreach ($cell in $row) {
            $xml.Add("<w:tc><w:p><w:r><w:t xml:space=`"preserve`">$(XmlEscape $cell)</w:t></w:r></w:p></w:tc>")
        }
        $xml.Add("</w:tr>")
    }
    $xml.Add("</w:tbl>")
    $xml.Add("<w:p/>")
    return ($xml -join "`n")
}

function ImageXml($rid, $cx, $cy, $name) {
    return @"
<w:p><w:pPr><w:jc w:val="center"/></w:pPr><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">
<wp:extent cx="$cx" cy="$cy"/><wp:docPr id="$rid" name="$(XmlEscape $name)"/>
<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="$rid" name="$(XmlEscape $name)"/><pic:cNvPicPr/></pic:nvPicPr>
<pic:blipFill><a:blip r:embed="rId$rid"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="$cx" cy="$cy"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
"@
}

function Get-ImageSizeEmu($path) {
    $img = [System.Drawing.Image]::FromFile($path)
    try {
        $maxCx = 8800000
        $maxCy = 5000000
        $ratio = [double]$img.Height / [double]$img.Width
        $cx = $maxCx
        $cy = [int]($cx * $ratio)
        if ($cy -gt $maxCy) {
            $cy = $maxCy
            $cx = [int]($cy / $ratio)
        }
        return @{ Cx = $cx; Cy = $cy }
    }
    finally {
        $img.Dispose()
    }
}

$images = @(
    @{ Name = "登录界面"; File = "8d1a5c95-1bc4-4095-beb1-6829723e464e.png" },
    @{ Name = "主界面"; File = "c0718a79-f28c-4337-be12-ec67f8cc3daf.png" },
    @{ Name = "导入客户方案"; File = "4e569ee9-18cb-4364-8b80-222a57a1cd96.png" },
    @{ Name = "基础配置"; File = "0d139cb6-dfe6-45b2-837f-409bc16db4f3.png" },
    @{ Name = "通讯设置"; File = "e387c6bb-8462-428c-8ce8-16a6c90abbb2.png" },
    @{ Name = "MES 配置"; File = "8d2afceb-f6aa-4cc2-a393-55e0dde1a9a0.png" },
    @{ Name = "插件配置"; File = "3b1f7a4d-334a-49e5-b506-9fda8b438f5e.png" },
    @{ Name = "MES 调试工具"; File = "eeef5e4f-7855-423c-aa32-441c4682d60e.png" },
    @{ Name = "用户配置"; File = "a384dc20-9582-4e08-b80a-f1b2aa6b9f8d.png" },
    @{ Name = "数据分析"; File = "95d195d2-477a-487d-92aa-d5978b8c7f23.png" },
    @{ Name = "软件帮助"; File = "c0d2a6af-dcbe-4955-8983-c83d4227727b.png" }
)

foreach ($image in $images) {
    $path = Join-Path $imageDir $image.File
    if (!(Test-Path $path)) {
        throw "缺少截图：$path"
    }
}

$sections = New-Object System.Collections.Generic.List[string]
$sections.Add((ParagraphXml "测昊-工业自动化测试工作站软件使用文档CH_V02" "Title"))
$sections.Add((ParagraphXml "适用软件：CeHao / 测昊工业自动化测试工作站"))
$sections.Add((ParagraphXml "适用对象：操作员、班组长、工程师、管理员"))
$sections.Add((ParagraphXml "文档日期：2026-07-09"))
$sections.Add((ParagraphXml "本文档按软件真实界面截图编写，围绕日常生产、参数配置、设备通讯、MES、插件导出、数据分析和异常处理进行说明。"))

$sections.Add((ParagraphXml "1. 软件概述" "Heading1"))
foreach ($b in @(
    "软件用于产线自动化测试工站，完成扫码、条码校验、下位机通讯、测试判定、MES 卡关/上传、结果导出和良率统计。",
    "操作员日常重点关注主界面的设备状态、SN、测试状态、FAIL 项、良率、测试项目表和运行日志。",
    "工程师和管理员负责客户方案导入、基础配置、通讯设置、MES 配置、插件配置和用户权限维护。"
)) { $sections.Add((BulletXml $b)) }

function Add-Section($title, $imageIndex, $intro, $headers, $rows, $notes = @()) {
    $sections.Add((PageBreakXml))
    $sections.Add((ParagraphXml $title "Heading1"))
    if (![string]::IsNullOrWhiteSpace($intro)) {
        $sections.Add((ParagraphXml $intro))
    }
    $image = $images[$imageIndex]
    $path = Join-Path $imageDir $image.File
    $size = Get-ImageSizeEmu $path
    $rid = $imageIndex + 1
    $sections.Add((ImageXml $rid $size.Cx $size.Cy $image.Name))
    $sections.Add((ParagraphXml "重点功能说明" "Heading2"))
    $sections.Add((TableXml $headers $rows))
    foreach ($note in $notes) {
        $sections.Add((BulletXml $note))
    }
}

Add-Section "2. 登录界面" 0 "登录界面用于输入账号密码并进入系统。不同账号对应不同操作权限。" `
    @("功能/配置", "说明") `
    @(
        @("账号", "输入登录账号，默认包含 operator、engineer、admin。"),
        @("密码", "输入对应密码。默认密码分别为 123456、123、admin，现场可在用户配置中修改。"),
        @("登录", "校验账号密码，通过后进入主界面。"),
        @("取消", "取消登录并关闭窗口。"),
        @("红色提示", "账号或密码为空、错误时显示提示，按提示重新输入。")
    ) `
    @("操作员账号用于日常生产；工程师账号用于参数维护；管理员账号用于用户权限、客户方案和管理员工具。")

Add-Section "3. 主界面" 1 "主界面是日常生产核心界面，用于查看设备状态、当前条码、测试状态、测试项目、良率和运行日志。" `
    @("区域/按钮", "说明") `
    @(
        @("扫码枪/下位机/MES 状态灯", "显示连接或在线状态；点击可连接、断开或切换 MES 在线/离线。"),
        @("隐藏日志/显示日志", "控制底部运行日志是否显示。"),
        @("管理员工具", "管理员可见，提供初始化、模拟扫码、模拟 PASS/FAIL 等调试操作。"),
        @("SN", "当前产品条码。"),
        @("FAIL项", "最近一次失败项目及失败值。"),
        @("WAIT/SCAN/READY/TEST/PASS/FAIL", "显示当前测试流程状态。"),
        @("清空良率", "清空本次总数、PASS、FAIL、连续 NG 和运行记录。"),
        @("测试项目表", "显示序号、测试项目、点位、上下限、单位、测试值和结果。"),
        @("运行日志", "记录登录、插件加载、设备连接、MES、导出、上传和报警信息。")
    ) `
    @("开班、换线或换工单前建议先点击清空良率。", "设备状态为红色时，应先检查通讯设置和设备连接。")

Add-Section "4. 导入客户方案" 2 "客户方案用于快速切换客户、产品、通讯、MES、导出插件和测试项配置。" `
    @("区域/按钮", "说明") `
    @(
        @("客户方案列表", "列出可导入的项目方案。"),
        @("MES 插件", "显示该方案使用的 MES 插件。"),
        @("导出插件", "显示该方案使用的结果导出插件。"),
        @("扫码枪/下位机", "显示该方案通讯摘要，例如 COM 口和波特率。"),
        @("备注", "显示方案来源、接口格式、导出格式等说明。"),
        @("导入", "应用选中方案。导入后 MES 在线模式默认置为离线，避免误连生产 MES。"),
        @("退出", "取消导入。")
    ) `
    @("换线或换客户时优先使用客户方案导入，然后再检查各项配置。")

Add-Section "5. 基础配置" 3 "基础配置维护客户、产品、测试类型、条码规则、弹窗和结果文件前缀等信息。" `
    @("配置项", "说明") `
    @(
        @("客户名称/产品名称", "用于窗口标题、方案信息、MES 参数和导出字段。"),
        @("测试类型/测试程式", "用于区分 ICT、FCT 等测试类别及当前测试程序版本。"),
        @("机台编号", "设备编号或机台 IP，部分 MES/导出插件会使用。"),
        @("条码长度", "扫码时校验条码长度。"),
        @("条码固定校验码", "用于固定码或模拟条码校验，无要求可留空。"),
        @("扫码规则前缀", "限制条码前缀；需勾选启用扫码前缀规则后生效。"),
        @("显示消息弹窗/弹窗需要确认", "控制测试结果、报警、异常提示是否弹窗以及是否必须确认。"),
        @("条码指定位验证", "检查条码指定位置是否属于允许内容，例如 L、B。"),
        @("条码限值联动", "根据条码某位内容自动调整指定测试项上下限。"),
        @("CSV 文件前缀", "设置默认 CSV 文件名前缀。")
    ) `
    @("条码规则修改后建议用一条测试条码验证。", "限值联动涉及判定结果，修改前应确认测试项序号和映射值。")

Add-Section "6. 通讯设置" 4 "通讯设置维护扫码枪和下位机的串口或 TCP 连接参数，以及自动触发、请求测试项和 OK 命令映射。" `
    @("配置项/按钮", "说明") `
    @(
        @("通讯方式", "支持 SerialPort、TcpClient、TcpServer、Disabled。"),
        @("串口号/波特率", "串口模式下配置 COM 口和通讯速率。"),
        @("IP 地址/端口", "TCP 模式下配置网络地址和端口。"),
        @("超时(ms)", "连接或通讯超时时间。"),
        @("自动扫码触发", "开启后系统主动发送扫码触发命令。"),
        @("扫码触发命令/停止命令", "扫码枪控制命令，支持 \\r\\n。"),
        @("连接后请求测试项", "下位机连接成功后自动请求测试项。"),
        @("请求测试项命令", "默认 Itemnum\\r\\n。"),
        @("条码内容到成功命令映射", "根据条码内容发送不同 OK 命令，例如 L=DMOK1\\r\\n;B=DMOK2\\r\\n。"),
        @("测试扫码枪连接/测试下位机连接", "按当前配置测试设备连接。")
    ) `
    @("串口设备连接失败时，先确认 COM 口是否被其他程序占用。", "TCP 通讯异常时，应检查设备 IP、端口、防火墙和网络连通性。")

Add-Section "7. MES 配置" 5 "MES 配置控制是否启用 MES、是否允许离线缓存、是否默认在线，并维护站位、账号、治具、工单和良率控制。" `
    @("配置项", "说明") `
    @(
        @("启用 MES", "开启后系统可执行 MES 卡关、在线切换和结果上传。"),
        @("允许离线缓存", "MES 不通时允许缓存，具体能力取决于 MES 插件。"),
        @("在线模式默认开启", "软件启动后默认在线，生产环境应谨慎开启。"),
        @("站位", "MES 卡关和上传使用的站位。"),
        @("MES 地址", "通用 MES 地址；部分插件会使用插件配置中的 URL 覆盖。"),
        @("工号/账号、密码", "MES 登录或上传使用的账号信息。"),
        @("箱号、治具、工单", "生产现场关键追溯信息。"),
        @("启用良率控制上传", "FAIL 结果上传前检查当前良率。"),
        @("上传阈值(%)", "良率低于阈值时禁止上传 FAIL 到 MES，并记录日志。")
    ) `
    @("切换在线前应确认 MES 插件、URL、账号、工单和站位正确。")

Add-Section "8. 插件配置" 6 "插件配置用于选择客户 MES 插件和结果导出插件，不同插件会显示不同参数。" `
    @("配置项", "说明") `
    @(
        @("当前插件", "选择当前 MES 插件。不选择插件时 MES 卡关和上传跳过。"),
        @("Login URL", "登录接口地址，部分 MES 插件使用。"),
        @("Pre-check URL", "测试前卡关接口，带 * 表示必填。"),
        @("Upload URL", "测试结果上传接口，带 * 表示必填。"),
        @("Timeout seconds", "请求超时时间，单位秒。"),
        @("Test type / Previous test type", "MES 使用的当前测试类型和前站测试类型。"),
        @("Product name / Work area / Site / Program / Lot", "客户 MES 所需的产品、区域、程序、批次等参数。"),
        @("导出器插件", "页面下方选择默认导出或客户导出插件，并配置导出路径、文件前缀、治具、软件版本等。")
    ) `
    @("插件参数直接影响 MES 卡关、上传和文件格式，修改后应先用调试工具或首件验证。")

Add-Section "9. MES 调试工具" 7 "MES 调试工具用于工程师手动发送 GET/POST 请求，验证 MES 接口和返回内容。" `
    @("功能/配置", "说明") `
    @(
        @("请求方法", "选择 GET 或 POST。"),
        @("URL", "请求接口地址，通常为卡关接口或上传接口。"),
        @("Content-Type", "POST 请求体格式，默认 application/json。"),
        @("GET 参数", "GET 请求参数，系统可根据当前配置生成默认卡关参数。"),
        @("POST Body", "POST 请求体，系统可生成默认上传 JSON。"),
        @("返回内容", "显示 MES 返回数据、HTTP 状态或异常信息。"),
        @("发送", "发起请求。"),
        @("清空返回", "清空返回内容和状态。"),
        @("退出", "关闭调试工具。")
    ) `
    @("调试前应确认使用测试条码和测试接口，避免向生产 MES 写入错误数据。")

Add-Section "10. 用户配置" 8 "用户配置用于维护操作模式、连续 NG 报警阈值、工号、治具编号和账号权限。" `
    @("配置项", "说明") `
    @(
        @("自动扫码", "开启后系统自动触发扫码枪。"),
        @("不扫码模式", "不依赖扫码枪，系统生成 NOSCAN 开头的条码，用于调试或特殊模式。"),
        @("连续 NG 报警数量", "连续 FAIL 达到阈值时弹窗报警并写入日志。"),
        @("登录工号", "当前操作员工号，可用于 MES 上传和调试默认参数。"),
        @("治具编号", "当前治具编号，可用于 MES 上传和导出文件。"),
        @("账号/显示名称/密码/角色", "维护登录账号和权限角色。")
    ) `
    @("账号权限修改后建议重新登录验证。")

Add-Section "11. 数据分析" 9 "数据分析页面显示当前运行期间的统计数据和失败记录。" `
    @("区域/字段", "说明") `
    @(
        @("总数", "当前统计周期测试总数。"),
        @("PASS", "当前 PASS 数。"),
        @("FAIL", "当前 FAIL 数。"),
        @("良率", "PASS / 总数。"),
        @("连续NG/最高失败项", "显示当前连续 NG 数和失败次数最高的测试项。"),
        @("本次失败记录", "显示时间、条码、结果、失败项、耗时和项目数。"),
        @("关闭", "关闭数据分析窗口。")
    ) `
    @("数据分析统计当前运行期间数据，换线或换工单前建议在主界面清空良率。")

Add-Section "12. 软件帮助" 10 "软件帮助页面显示当前版本、客户、产品、MES 插件和版本迭代说明。" `
    @("区域/字段", "说明") `
    @(
        @("当前版本", "显示当前软件版本，例如 CH_V02。"),
        @("客户/产品", "显示当前配置中的客户和产品。"),
        @("MES插件", "显示当前使用的 MES 插件。"),
        @("版本迭代", "显示本版本主要更新内容。"),
        @("关闭", "关闭帮助窗口。")
    ) `
    @("排查现场版本差异时，可先打开软件帮助确认版本号。")

$sections.Add((PageBreakXml))
$sections.Add((ParagraphXml "13. 测试项配置" "Heading1"))
$sections.Add((ParagraphXml "当前截图目录中未包含测试项配置页面截图。本页用于维护测试项目列表、顺序、上下限、单位和点位。"))
$sections.Add((TableXml @("按钮/列", "说明") @(
    @("新增", "新增一条测试项。"),
    @("删除", "删除选中的测试项。"),
    @("上移/下移", "调整测试项顺序。"),
    @("顺序", "测试项排序。"),
    @("测试项目", "测试项名称，应与下位机返回项目匹配。"),
    @("下限/上限", "判定限值。"),
    @("单位", "测试单位。"),
    @("点位A/点位B", "测试点位信息。"),
    @("保存/退出", "保存配置或放弃修改。")
)))

$sections.Add((ParagraphXml "14. 日常操作流程" "Heading1"))
foreach ($b in @(
    "开班前：登录系统，确认客户、产品、测试类型、测试程式、站位、工单、治具和设备状态。",
    "生产前：确认测试项目和上下限，连接扫码枪、下位机，按需要切换 MES 在线，清空良率。",
    "测试中：扫码后查看 SN、测试状态、FAIL 项、测试项目表和运行日志。",
    "换线或换工单：导入客户方案或修改配置，保存后重新连接设备，并用首件验证流程。",
    "发生异常：优先查看运行日志，再检查通讯设置、MES 配置、插件配置和导出路径。"
)) { $sections.Add((BulletXml $b)) }

$sections.Add((ParagraphXml "15. 常见异常处理" "Heading1"))
$sections.Add((TableXml @("现象", "可能原因", "处理方法") @(
    @("扫码枪未连接", "COM 口错误、设备未上电、串口被占用、TCP 参数错误。", "检查通讯设置，点击状态灯重连或测试扫码枪连接。"),
    @("下位机未连接", "COM 口、波特率、IP、端口错误，下位机未启动。", "检查通讯设置和设备状态，使用测试下位机连接。"),
    @("扫码后无反应", "条码长度/前缀不符合规则，自动扫码未开启，下位机未收到命令。", "检查条码规则、触发命令和 OK 命令映射。"),
    @("MES 无法在线", "MES 未启用、插件未选择、URL 错误、账号密码错误、网络不通。", "检查 MES 配置和插件配置，用 MES 调试工具验证接口。"),
    @("MES 卡关失败", "条码未过前站、工单/站位/治具错误、MES 业务失败。", "查看运行日志和 MES 返回内容，按现场 MES 流程处理。"),
    @("导出文件未生成", "路径不存在、无权限、插件参数错误。", "检查保存路径和导出插件配置。"),
    @("良率不正确", "未清空上一批统计或调试数据计入。", "换线、换工单或开班前点击清空良率。"),
    @("连续 NG 报警", "连续失败达到阈值。", "停线检查治具、线材、测试程序、下位机和产品状态。")
)))

$docXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
<w:body>
$($sections -join "`n")
<w:sectPr><w:pgSz w:w="16838" w:h="11906" w:orient="landscape"/><w:pgMar w:top="720" w:right="720" w:bottom="720" w:left="720" w:header="360" w:footer="360" w:gutter="0"/></w:sectPr>
</w:body></w:document>
"@

$stylesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:pPr><w:spacing w:after="120"/></w:pPr><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:sz w:val="22"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:jc w:val="center"/><w:spacing w:after="360"/></w:pPr><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:b/><w:sz w:val="40"/><w:color w:val="0F4C81"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:pPr><w:spacing w:before="240" w:after="160"/></w:pPr><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:b/><w:sz w:val="30"/><w:color w:val="0F4C81"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:pPr><w:spacing w:before="180" w:after="120"/></w:pPr><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:b/><w:sz w:val="24"/><w:color w:val="1D4ED8"/></w:rPr></w:style>
<w:style w:type="table" w:styleId="TableGrid"><w:name w:val="Table Grid"/><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:color="9CCFEF"/><w:left w:val="single" w:sz="4" w:color="9CCFEF"/><w:bottom w:val="single" w:sz="4" w:color="9CCFEF"/><w:right w:val="single" w:sz="4" w:color="9CCFEF"/><w:insideH w:val="single" w:sz="4" w:color="CFE8F8"/><w:insideV w:val="single" w:sz="4" w:color="CFE8F8"/></w:tblBorders></w:tblPr></w:style>
</w:styles>
"@

$numberingXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:numbering xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="hybridMultilevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="•"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum>
<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>
</w:numbering>
"@

$relsList = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $images.Count; $i++) {
    $rid = $i + 1
    $target = "media/" + $images[$i].File
    $relsList.Add("<Relationship Id=`"rId$rid`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image`" Target=`"$target`"/>")
}
$styleRid = $images.Count + 1
$numberingRid = $images.Count + 2
$relsList.Add("<Relationship Id=`"rId$styleRid`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles`" Target=`"styles.xml`"/>")
$relsList.Add("<Relationship Id=`"rId$numberingRid`" Type=`"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering`" Target=`"numbering.xml`"/>")
$rels = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><Relationships xmlns=`"http://schemas.openxmlformats.org/package/2006/relationships`">$($relsList -join '')</Relationships>"

$rootRels = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
"@

$contentTypes = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Default Extension="png" ContentType="image/png"/>
<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>
</Types>
"@

if (Test-Path $docxPath) { Remove-Item $docxPath -Force }
$zip = [System.IO.Compression.ZipFile]::Open($docxPath, [System.IO.Compression.ZipArchiveMode]::Create)

function Add-TextEntry($zip, $name, $text) {
    $entry = $zip.CreateEntry($name)
    $stream = $entry.Open()
    $writer = New-Object System.IO.StreamWriter($stream, [System.Text.Encoding]::UTF8)
    $writer.Write($text)
    $writer.Dispose()
    $stream.Dispose()
}

function Add-FileEntry($zip, $name, $path) {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $path, $name) | Out-Null
}

Add-TextEntry $zip "[Content_Types].xml" $contentTypes
Add-TextEntry $zip "_rels/.rels" $rootRels
Add-TextEntry $zip "word/document.xml" $docXml
Add-TextEntry $zip "word/styles.xml" $stylesXml
Add-TextEntry $zip "word/numbering.xml" $numberingXml
Add-TextEntry $zip "word/_rels/document.xml.rels" $rels
foreach ($image in $images) {
    Add-FileEntry $zip ("word/media/" + $image.File) (Join-Path $imageDir $image.File)
}
$zip.Dispose()

Write-Output $docxPath

