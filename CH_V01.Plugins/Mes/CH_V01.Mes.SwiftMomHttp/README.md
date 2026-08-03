# SwiftMOM HTTP MES 示例插件

插件类：`CH_V01.Mes.SwiftMomHttp.SwiftMomHttpMesPlugin`

## 支持能力

- 测试连接：POST `clientLogin`
- 登录取 Token：POST `clientLogin`
- 测试前卡关：POST `checkRoute`
- 测试结果上传：POST `createRoute`
- 离线缓存：插件声明支持，缓存队列由主程序实现

## 配置字段

- `BaseUrl`：默认 `http://127.0.0.1:8080/swiftmom/api/`
- `TimeoutSeconds`：默认 `5`
- `Password`：结果上传时如果未传 Token，会用它重新登录
- `Token`：可选，外部已登录后可直接传入

## 协议字段

### clientLogin

```json
{
  "userNo": "工号",
  "password": "密码"
}
```

成功时要求返回：

```json
{
  "msgId": "0",
  "msgStr": "成功信息",
  "token": "..."
}
```

### checkRoute

```json
{
  "pcbSeq": "条码",
  "prodNo": "工单",
  "stationNo": "站位",
  "retest": "是否复测"
}
```

请求头：`token`

### createRoute

```json
{
  "pcbSeq": "条码",
  "prodNo": "工单",
  "stationNo": "站位",
  "result": "PASS/FAIL",
  "remark": "备注",
  "testItem": "测试项JSON字符串",
  "userNo": "工号",
  "weight": "重量",
  "packNo": "箱号",
  "rmk1": "",
  "rmk2": "",
  "rmk3": "",
  "rmk4": ""
}
```

请求头：`token`

## 说明

该插件根据 `SwiftMOM工序过站接口函数（Http)说明20221029(1).pdf` 中的接口 URL，以及旧版本中已经使用的 `clientLogin/checkRoute/createRoute` 字段实现。后续如果客户现场协议字段有差异，应新增插件或派生适配类，不建议在主程序中写客户判断。
