$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$imageDir = Join-Path $root "images"
New-Item -ItemType Directory -Force $imageDir | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-Brush($hex) {
    return New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function New-Pen($hex, $width = 1) {
    return New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($hex)), $width
}

function Draw-Text($g, $text, $x, $y, $size = 16, $color = "#111827", $bold = $false, $width = 400, $height = 30) {
    $style = if ($bold) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }
    $font = New-Object System.Drawing.Font("Microsoft YaHei UI", $size, $style)
    $brush = New-Brush $color
    $rect = New-Object System.Drawing.RectangleF($x, $y, $width, $height)
    $g.DrawString($text, $font, $brush, $rect)
    $font.Dispose()
    $brush.Dispose()
}

function Fill-RoundRect($g, $x, $y, $w, $h, $fill, $border = "#7DD3FC") {
    $brush = New-Brush $fill
    $pen = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($border)), 1
    $rect = New-Object System.Drawing.Rectangle($x, $y, $w, $h)
    $g.FillRectangle($brush, $rect)
    $g.DrawRectangle($pen, $rect)
    $brush.Dispose()
    $pen.Dispose()
}

function Draw-Input($g, $label, $value, $x, $y, $w = 300) {
    Draw-Text $g $label $x $y 13 "#52606D" $false $w 22
    Fill-RoundRect $g $x ($y + 24) $w 34 "#F8FCFF" "#BAE6FD"
    Draw-Text $g $value ($x + 10) ($y + 30) 13 "#111827" $false ($w - 20) 24
}

function Save-Canvas($path, $draw) {
    $bmp = New-Object System.Drawing.Bitmap(1280, 800)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml("#EAF7FF"))
    & $draw $g
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

Save-Canvas (Join-Path $imageDir "01-login.png") {
    param($g)
    Draw-Text $g "欢迎使用测昊测试系统" 96 250 32 "#174A63" $true 520 52
    Draw-Text $g "让每一次测试更精准、更高效、更可靠。" 100 318 17 "#34657A" $false 520 38
    Fill-RoundRect $g 760 150 390 500 "#F3FBFF" "#38BDF8"
    Draw-Text $g "用户登录" 805 198 24 "#15384A" $true 260 36
    Draw-Text $g "输入账号密码后进入系统" 805 238 14 "#536D7A" $false 260 26
    Draw-Input $g "账号" "admin" 805 300 300
    Draw-Input $g "密码" "admin" 805 390 300
    Fill-RoundRect $g 880 535 100 38 "#0EA5E9" "#0EA5E9"
    Draw-Text $g "登录" 912 542 14 "#FFFFFF" $true 70 24
    Fill-RoundRect $g 1000 535 100 38 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "取消" 1032 542 14 "#111827" $false 70 24
}

Save-Canvas (Join-Path $imageDir "02-main.png") {
    param($g)
    Fill-RoundRect $g 0 0 1280 42 "#BAE6FD" "#38BDF8"
    Draw-Text $g "系统管理    用户操作    数据分析    软件帮助    切换登录" 24 10 14 "#111827" $false 650 24
    Fill-RoundRect $g 0 42 1280 64 "#FFFFFF" "#38BDF8"
    Draw-Text $g "默认客户 - 默认产品" 470 58 28 "#1D4ED8" $false 420 42
    Fill-RoundRect $g 24 126 550 245 "#FFFFFF" "#38BDF8"
    Draw-Text $g "SN:" 48 154 18 "#374151" $false 80 28
    Fill-RoundRect $g 120 148 420 36 "#F0F9FF" "#BAE6FD"
    Draw-Text $g "等待扫码..." 136 154 16 "#111827" $false 240 26
    Draw-Text $g "FAIL项:" 48 205 18 "#374151" $false 90 28
    Fill-RoundRect $g 120 199 420 36 "#F0F9FF" "#BAE6FD"
    Draw-Text $g "连接状态" 48 258 16 "#111827" $true 120 28
    Draw-Text $g "● 扫码枪 未连接       ● 下位机 未连接       ● MES 离线" 65 300 14 "#52606D" $false 470 28
    Fill-RoundRect $g 600 126 300 245 "#FFFFFF" "#38BDF8"
    Draw-Text $g "测试状态" 624 150 18 "#111827" $true 140 30
    Draw-Text $g "待机" 700 205 50 "#2563EB" $true 140 70
    Draw-Text $g "初始化   模拟扫码   模拟PASS   模拟FAIL" 625 315 13 "#111827" $false 260 26
    Fill-RoundRect $g 925 126 330 245 "#FFFFFF" "#38BDF8"
    Draw-Text $g "MES / 生产信息" 950 150 18 "#111827" $true 200 30
    Draw-Text $g "站位:        工单:" 950 200 15 "#374151" $false 240 28
    Draw-Text $g "总数: 0      Pass / Fail: 0 / 0      良率: 0.00%" 950 252 15 "#374151" $false 280 80
    Fill-RoundRect $g 24 395 440 360 "#FFFFFF" "#38BDF8"
    Draw-Text $g "运行日志" 48 420 18 "#111827" $true 140 30
    Draw-Text $g "[08:30:00] 系统启动`n[08:30:05] 等待连接设备`n[08:30:10] 等待扫码或下位机信号" 48 470 14 "#334155" $false 360 120
    Fill-RoundRect $g 488 395 767 360 "#FFFFFF" "#38BDF8"
    Draw-Text $g "测试项目" 512 420 18 "#111827" $true 140 30
    Draw-Text $g "序号        测试项目              上限        下限        测试值        单位        结果" 512 465 14 "#111827" $true 690 26
    Draw-Text $g "1           J1_1-SHORT            10          0                         ohm`n2           R1                    11.000      9.000                     Kohm`n3           U1-ID                 0x38        0x38                      HEX" 512 510 14 "#334155" $false 690 100
}

Save-Canvas (Join-Path $imageDir "03-basic-config.png") {
    param($g)
    Draw-Text $g "基础配置" 42 30 28 "#1D4ED8" $true 240 44
    Fill-RoundRect $g 42 92 1190 250 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "系统设置" 70 120 18 "#111827" $true 160 30
    Draw-Input $g "客户名称" "默认客户" 70 165 330
    Draw-Input $g "产品名称" "默认产品" 430 165 330
    Draw-Input $g "测试类型" "ICT" 790 165 330
    Draw-Input $g "测试程式" "FCT-V1.0" 70 245 330
    Draw-Input $g "条码长度" "12" 430 245 330
    Draw-Input $g "CSV 文件前缀" "F3409-0021-0021CS" 790 245 330
    Fill-RoundRect $g 42 370 1190 160 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "导出器插件" 70 398 18 "#111827" $true 160 30
    Draw-Input $g "当前导出器" "默认导出 / 客户导出插件" 70 445 520
    Draw-Text $g "不选择客户导出器时使用默认 CSV、DCL、CsvMes；选择后只执行客户导出插件。" 620 475 14 "#64748B" $false 550 48
    Fill-RoundRect $g 42 558 1190 150 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "保存路径" 70 586 18 "#111827" $true 160 30
    Draw-Input $g "运行日志路径" "D:\VisionResult\FPC\Log\" 70 635 340
    Draw-Input $g "DCL/TXT 保存路径" "D:\VisionResult\FPC\ProOut\" 450 635 340
    Draw-Input $g "产品 CSV 保存路径" "D:\VisionResult\FPC\ProData\" 830 635 340
}

Save-Canvas (Join-Path $imageDir "04-communication.png") {
    param($g)
    Draw-Text $g "通讯设置" 42 30 28 "#1D4ED8" $true 240 44
    Fill-RoundRect $g 60 105 560 560 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "扫码枪" 90 132 20 "#111827" $true 160 32
    Draw-Input $g "通讯方式" "SerialPort / TcpClient / TcpServer / Disabled" 90 180 460
    Draw-Input $g "串口号" "COM9" 90 270 210
    Draw-Input $g "波特率" "9600" 330 270 210
    Draw-Input $g "IP 地址" "127.0.0.1" 90 360 210
    Draw-Input $g "端口" "0" 330 360 210
    Draw-Input $g "超时(ms)" "3000" 90 450 210
    Draw-Text $g "☑ 自动扫码触发     扫码触发命令: start\\r\\n" 90 545 14 "#374151" $false 460 26
    Fill-RoundRect $g 670 105 560 560 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "下位机" 700 132 20 "#111827" $true 160 32
    Draw-Input $g "通讯方式" "SerialPort / TcpClient / TcpServer / Disabled" 700 180 460
    Draw-Input $g "串口号" "COM11" 700 270 210
    Draw-Input $g "波特率" "115200" 940 270 210
    Draw-Input $g "IP 地址" "127.0.0.1" 700 360 210
    Draw-Input $g "端口" "15000" 940 360 210
    Draw-Input $g "超时(ms)" "3000" 700 450 210
    Draw-Text $g "☑ 连接后请求测试项     请求命令: Itemnum\\r\\n" 700 545 14 "#374151" $false 460 26
}

Save-Canvas (Join-Path $imageDir "05-mes-config.png") {
    param($g)
    Draw-Text $g "MES 设置" 42 30 28 "#1D4ED8" $true 240 44
    Fill-RoundRect $g 42 92 1190 300 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "MES 设置" 70 122 18 "#111827" $true 160 30
    Draw-Text $g "☑ 启用 MES        ☑ 允许离线缓存        ☐ 在线模式默认开启" 70 168 15 "#374151" $false 760 28
    Draw-Input $g "MES 插件" "SwiftMom.Http / JiaLianYi.CareerRule / SuzhouWeiXin.Http" 70 220 480
    Draw-Input $g "MES 地址" "http://127.0.0.1:8080/swiftmom/api/" 590 220 480
    Draw-Input $g "工号/账号" "" 70 310 230
    Draw-Input $g "密码" "" 330 310 230
    Draw-Input $g "工单" "" 590 310 230
    Draw-Input $g "治具" "" 850 310 230
    Fill-RoundRect $g 42 430 1190 150 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "插件参数" 70 460 18 "#111827" $true 160 30
    Draw-Text $g "插件会按所选客户暴露参数，例如 PreCheckUrl、UploadUrl、PartNumber、Station、Tooling、JobNo 等。" 70 510 15 "#374151" $false 1000 32
    Fill-RoundRect $g 42 615 1190 110 "#FFFFFF" "#7DD3FC"
    Draw-Text $g "良率控制" 70 645 18 "#111827" $true 160 30
    Draw-Text $g "☐ 启用良率控制上传     上传阈值(%): 90" 70 690 15 "#374151" $false 520 30
}

Save-Canvas (Join-Path $imageDir "06-data-analysis.png") {
    param($g)
    Draw-Text $g "数据分析" 42 30 28 "#1D4ED8" $true 240 44
    $x = 42
    foreach ($item in @("总数`n0","PASS`n0","FAIL`n0","良率`n0.00%","连续NG/最高失败项`n0")) {
        Fill-RoundRect $g $x 100 220 110 "#FFFFFF" "#CBD5E1"
        $parts = $item -split "`n"
        Draw-Text $g $parts[0] ($x+18) 120 14 "#52606D" $false 180 24
        Draw-Text $g $parts[1] ($x+18) 150 26 "#111827" $true 180 38
        $x += 238
    }
    Fill-RoundRect $g 42 250 1190 440 "#FFFFFF" "#CBD5E1"
    Draw-Text $g "本次失败记录" 70 280 20 "#111827" $true 240 34
    Draw-Text $g "时间                       条码                       结果      失败项              耗时(s)    项目数" 70 340 14 "#111827" $true 1000 28
    Draw-Text $g "2026/07/03 08:35:22        SN20260703001              NG        R1                 4.2        3`n2026/07/03 08:36:10        SN20260703002              PASS                         3.8        3" 70 390 14 "#334155" $false 1000 70
}

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

function ImageXml($rid, $cx, $cy, $name) {
    return @"
<w:p><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">
<wp:extent cx="$cx" cy="$cy"/><wp:docPr id="$rid" name="$(XmlEscape $name)"/>
<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="$rid" name="$(XmlEscape $name)"/><pic:cNvPicPr/></pic:nvPicPr>
<pic:blipFill><a:blip r:embed="rId$rid"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>
<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="$cx" cy="$cy"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>
</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>
"@
}

$sections = New-Object System.Collections.Generic.List[string]
$sections.Add((ParagraphXml "CH_V03 测试系统使用文档" "Title"))
$sections.Add((ParagraphXml "适用范围：CH_V03.App 桌面端、MES 插件、导出插件、项目方案配置。"))
$sections.Add((ParagraphXml "生成日期：2026-07-03"))
$sections.Add((ParagraphXml "1. 系统概述" "Heading1"))
$sections.Add((ParagraphXml "CH_V03 是一套用于产线测试工站的 WPF 桌面系统，围绕扫码、下位机通信、测试项目解析、MES 卡关/上传、结果导出和生产数据分析展开。系统通过插件和项目方案支持不同客户、产品、测试类型和现场接口。"))
$sections.Add((ParagraphXml "2. 登录与权限" "Heading1"))
$sections.Add((ParagraphXml "默认账号来自配置：operator/123456、engineer/123、admin/admin。操作员用于日常生产；工程师可配置系统和测试项；管理员拥有用户配置、项目方案导入等完整权限。"))
$sections.Add((ImageXml 1 5486400 3429000 "登录界面"))
$sections.Add((ParagraphXml "3. 主界面功能" "Heading1"))
$sections.Add((ParagraphXml "主界面集中显示当前条码、失败项、扫码枪/下位机/MES 连接状态、测试状态、站位、工单、总数、Pass/Fail、良率、运行日志和测试项目明细。常用操作包括连接设备、断开设备、切换在线模式、初始化、模拟扫码、模拟 PASS、模拟 FAIL 和清空良率。"))
$sections.Add((ImageXml 2 5486400 3429000 "主界面"))
$sections.Add((ParagraphXml "4. 标准操作流程" "Heading1"))
foreach ($b in @(
    "登录系统，确认右下角显示当前账号和角色。",
    "进入系统管理，按现场项目确认基础配置、通讯设置、MES 设置和测试项配置。",
    "在主界面点击连接设备，确认扫码枪、下位机、MES 状态符合生产要求。",
    "扫码或接收下位机 DM 启动信号；启用 MES 时先执行卡关，卡关通过后继续测试。",
    "测试完成后系统解析项目结果，刷新良率和失败项，按配置导出 CSV/DCL/CsvMes 或客户插件文件，并上传 MES。",
    "生产结束或换线时保存配置，必要时导入对应客户方案。"
)) { $sections.Add((BulletXml $b)) }
$sections.Add((ParagraphXml "5. 基础配置" "Heading1"))
$sections.Add((ParagraphXml "基础配置用于维护客户名称、产品名称、测试类型、测试程式、站位编号、机台编号、条码长度、条码固定校验码、扫码规则前缀、CSV 文件前缀、消息弹窗、导出器插件和保存路径。默认导出包含 CSV、DCL、CsvMes；选择客户导出器后，测试完成时只执行该客户导出插件。"))
$sections.Add((ImageXml 3 5486400 3429000 "基础配置"))
$sections.Add((ParagraphXml "6. 通讯设置" "Heading1"))
$sections.Add((ParagraphXml "扫码枪和下位机均支持 SerialPort、TcpClient、TcpServer、Disabled。串口模式需配置 COM 口和波特率；TCP 模式需配置 IP、端口和超时。扫码枪可启用自动触发命令；下位机可启用连接后请求测试项命令。"))
$sections.Add((ImageXml 4 5486400 3429000 "通讯设置"))
$sections.Add((ParagraphXml "7. MES 设置" "Heading1"))
$sections.Add((ParagraphXml "MES 设置包含启用 MES、允许离线缓存、在线模式默认开启、MES 插件、MES 地址、工号/账号、密码、箱号、治具、工单及插件参数。当前代码支持插件式 MES：插件实现 IMesPlugin，提供连接测试、登录、测试前卡关和结果上传。"))
$sections.Add((ImageXml 5 5486400 3429000 "MES 设置"))
$sections.Add((ParagraphXml "良率控制可配置是否限制上传以及上传阈值。现场启用前应确认分母口径、拦截规则和 MES 对失败上传的处理方式。"))
$sections.Add((ParagraphXml "8. 测试项配置" "Heading1"))
$sections.Add((ParagraphXml "测试项配置维护顺序、测试项目、下限、上限、单位、点位 A、点位 B。可新增、删除、上移、下移项目。主界面测试明细会根据这些项目展示测试值和 OK/NG 结果。"))
$sections.Add((ParagraphXml "9. 项目方案导入" "Heading1"))
$sections.Add((ParagraphXml "客户方案 JSON 放置在 CH_V03.Plugins/profiles 目录。方案包含客户、产品、测试类型、通讯参数、MES 插件参数、导出插件参数、路径、弹窗、测试项和用户设置。导入客户方案可快速切换到对应产线配置。"))
$sections.Add((ParagraphXml "当前仓库包含方案：JiaLianYi_0033、KunshanJiaLianYi_MAODONG_Audio、KunshanJiaLianYi_MAODONG_ICT、KunshanJiaLianYi_MAODONG_Sealing、SuzhouWeiXin_18165_ICT。"))
$sections.Add((ParagraphXml "10. 用户配置" "Heading1"))
$sections.Add((ParagraphXml "用户配置可维护自动扫码、不扫码模式、连续 NG 报警数量、登录工号、治具编号，以及账号、显示名称、密码和角色。修改账号权限后应保存配置并重新登录验证。"))
$sections.Add((ParagraphXml "11. 数据分析" "Heading1"))
$sections.Add((ParagraphXml "数据分析页面显示总数、PASS、FAIL、良率、连续 NG、最高失败项和本次失败记录，可用于现场快速判断良率异常和集中失败项目。"))
$sections.Add((ImageXml 6 5486400 3429000 "数据分析"))
$sections.Add((ParagraphXml "12. 输出文件与日志" "Heading1"))
foreach ($b in @(
    "CsvDirectory：产品 CSV 保存目录，默认 D:\VisionResult\FPC\ProData\。",
    "DclDirectory：DCL/TXT 保存目录，默认 D:\VisionResult\FPC\ProOut\。",
    "LogDirectory：运行日志目录，默认 D:\VisionResult\FPC\Log\。",
    "CsvName：CSV 文件名前缀，按项目或客户要求配置。"
)) { $sections.Add((BulletXml $b)) }
$sections.Add((ParagraphXml "13. 插件说明" "Heading1"))
$sections.Add((ParagraphXml "MES 插件 DLL 放在程序目录 plugins\mes 下，导出插件 DLL 放在 plugins\export 下。第三方库放在 CH_V03.Plugins/libs 或项目方案指定目录。SelectedPlugin 或 EnabledPluginIds 必须与插件 Id 一致。"))
$sections.Add((ParagraphXml "14. 常见问题" "Heading1"))
foreach ($b in @(
    "扫码枪或下位机连接失败：检查通讯方式、COM 口、波特率、IP、端口、防火墙和设备占用情况。",
    "MES 卡关失败：检查 MES 地址、账号密码、工单、站位、插件参数和网络连通性。",
    "测试项为空或不匹配：检查下位机发送格式、协议适配器和测试项配置。",
    "导出文件未生成：检查保存路径是否存在、是否有写入权限、导出插件是否选择正确。",
    "良率显示异常：确认是否清空过良率，以及 PASS/FAIL 记录是否来自当前生产批次。"
)) { $sections.Add((BulletXml $b)) }

$docXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
 xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
<w:body>
$($sections -join "`n")
<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="850" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>
</w:body></w:document>
"@

$stylesXml = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:sz w:val="22"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:pPr><w:jc w:val="center"/><w:spacing w:after="360"/></w:pPr><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:b/><w:sz w:val="40"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:pPr><w:spacing w:before="360" w:after="160"/></w:pPr><w:rPr><w:rFonts w:ascii="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/><w:b/><w:sz w:val="30"/><w:color w:val="1D4ED8"/></w:rPr></w:style>
</w:styles>
"@

$rels = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/01-login.png"/>
<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/02-main.png"/>
<Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/03-basic-config.png"/>
<Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/04-communication.png"/>
<Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/05-mes-config.png"/>
<Relationship Id="rId6" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/06-data-analysis.png"/>
<Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
"@

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
</Types>
"@

$docxPath = Join-Path $root "CH_V03_测试系统使用文档.docx"
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
Add-TextEntry $zip "word/_rels/document.xml.rels" $rels
foreach ($img in Get-ChildItem $imageDir -Filter *.png | Sort-Object Name) {
    Add-FileEntry $zip ("word/media/" + $img.Name) $img.FullName
}
$zip.Dispose()

Write-Output $docxPath
