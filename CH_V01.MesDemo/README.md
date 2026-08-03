# CH_V01.MesDemo

苏州维信 `SuzhouWeiXin.Http` 插件的本地 MES Demo 服务，可部署到本机 IIS。

## 接口

- `GET /api/ettestrecords/prevalidation`
  - 对应插件配置 `PreCheckUrl`
  - 返回 `isSuccess/message/result`
- `POST /api/ettestrecords`
  - 对应插件配置 `UploadUrl`
  - 接收测试结果 JSON，返回 `isSuccess/message/result`
- `GET /api/demo/logs`
  - 查看最近 50 条请求日志
- `DELETE /api/demo/logs`
  - 清空请求日志
- `GET /health`
  - 服务健康检查

## 本地运行

```powershell
dotnet run --project .\CH_V01.MesDemo\CH_V01.MesDemo.csproj --urls http://127.0.0.1:5088
```

CH_V01 中苏州维信 MES 插件配置：

```text
PreCheckUrl = http://127.0.0.1:5088/api/ettestrecords/prevalidation
UploadUrl   = http://127.0.0.1:5088/api/ettestrecords
```

## IIS 发布

先发布：

```powershell
dotnet publish .\CH_V01.MesDemo\CH_V01.MesDemo.csproj -c Release -o .\publish\CH_V01.MesDemo
```

IIS 部署要点：

1. 安装 .NET 8 Hosting Bundle。
2. 新建站点或应用程序，物理路径指向 `publish\CH_V01.MesDemo`。
3. 应用程序池设置为“无托管代码”。
4. 绑定一个端口，例如 `http://127.0.0.1:5088`。

部署到 IIS 后，CH_V01 配置：

```text
PreCheckUrl = http://127.0.0.1:5088/api/ettestrecords/prevalidation
UploadUrl   = http://127.0.0.1:5088/api/ettestrecords
```

## Demo 规则

- 条码为空：卡关失败。
- 条码包含 `NG` 或 `FAIL`：卡关失败。
- 上传结果为 `FAIL` 或 `NG`：上传接口返回失败。
- 其他情况返回成功。
