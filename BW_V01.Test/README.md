# BW_V01.Test

TCP 下位机模拟测试程序。本程序作为 TCP Server 运行，BW_V01 测试系统作为 TCP Client 连接进来。

## 运行

```powershell
dotnet run --project .\BW_V01.Test\BW_V01.Test.csproj
```

默认监听 `127.0.0.1:15000`。

指定端口和监听地址：

```powershell
dotnet run --project .\BW_V01.Test\BW_V01.Test.csproj -- 15000 0.0.0.0
```

## 测试系统配置

在 BW_V01 测试系统中，将下位机通讯方式配置为：

- 通讯方式：`TcpClient`
- IP：测试程序监听的地址
- 端口：测试程序监听的端口，默认 `15000`

如果测试系统和测试程序在同一台电脑，IP 可使用 `127.0.0.1`。

## 命令

- `/start`：发送启动信号 `DM\r\n`
- `/items`：发送测试项列表
- `/pass`：发送 PASS 测试结果
- `/fail`：发送 FAIL 测试结果
- `/demo`：依次发送启动信号、测试项列表、PASS 结果
- `/send <text>`：发送自定义报文，支持 `\r` 和 `\n`
- `/quit`：退出程序

## 注意

如果 BW_V01 中点击的是“通讯测试”，客户端会在连接成功后立即断开，这是短连接验证行为。要持续接收本程序发送的测试项和测试结果，需要在 BW_V01 主界面执行设备连接，让下位机连接保持在线。

常用联调顺序：

1. 启动本程序。
2. BW_V01 下位机配置为 `TcpClient`，IP/端口指向本程序。
3. 在 BW_V01 中执行设备连接，看到本程序显示 `Client connected`。
4. 在本程序输入 `/items` 发送测试项，或输入 `/pass`、`/fail` 发送测试结果。
