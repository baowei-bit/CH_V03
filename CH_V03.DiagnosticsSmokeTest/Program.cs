using CH_V03.App.Services;
using CH_V03.Core.Configuration;
using CH_V03.Core.TestFlow;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }
    checks++;
    Console.WriteLine($"PASS: {name}");
}

void Reject(Action action, string expectedReason)
{
    try
    {
        action();
    }
    catch (FormatException exception)
    {
        Check(exception.Message.Contains(expectedReason, StringComparison.Ordinal), expectedReason);
        return;
    }
    throw new InvalidOperationException($"Expected rejection: {expectedReason}");
}

var configuration = new UserConfiguration();
var authentication = new AuthenticationService();
Check(authentication.Login(configuration, "zuzhuang", "123456", out _), "diagnostic account login");
Check(authentication.CurrentSession.IsDiagnosticMode && !authentication.CurrentSession.CanManageUsers, "diagnostic mode without admin permissions");
Check(!authentication.Login(configuration, "zuzhuang", "wrong", out _), "wrong password rejected");
Check(authentication.Login(configuration, "operator", "123456", out _) && !authentication.CurrentSession.IsDiagnosticMode, "normal account retains normal mode");
configuration.Accounts.RemoveAll(account => account.UserName == "zuzhuang");
configuration.EnsureDefaultAccounts();
configuration.EnsureDefaultAccounts();
Check(configuration.Accounts.Count(account => account.UserName == "zuzhuang") == 1, "old configuration migration is idempotent");
configuration.Accounts.Single(account => account.UserName == "zuzhuang").Password = "changed";
configuration.EnsureDefaultAccounts();
Check(authentication.Login(configuration, "ZUZHUANG", "changed", out _) && authentication.CurrentSession.IsDiagnosticMode, "custom password preserved and account name is case insensitive");
authentication.Logout();
Check(!authentication.CurrentSession.IsDiagnosticMode, "logout disables diagnosis");

var adapter = new DefaultIctProtocolAdapter();
var flow = new TestFlowService(adapter);
Check(flow.LoadTestItems("Item1,Voltage,5,0,V;1;end").Items.Count == 1, "valid header with count trailer");
Reject(() => flow.LoadTestItems("Item1,Voltage;end"), "至少 5 个字段");
Check(flow.Snapshot().Items.Single().ItemName == "Voltage", "malformed header preserves previous items");
Check(flow.LoadTestItems("Item1,Voltage,5,0,V;Item3,Current,2,0,A;end").Message.Contains("连续"), "non-contiguous header explains reason");
Reject(() => adapter.ParseResults("Test1,5;end"), "至少 3 个字段");
Reject(() => adapter.ParseResults("TestX,5,OK;end"), "正整数");
Reject(() => flow.CompleteTest(";end"), "未解析到有效测试结果");
Reject(() => flow.CompleteTest("Test1,5,OK;Test1,5,OK;end"), "重复");
Check(flow.Snapshot().IsPass is null, "invalid results never become PASS");
Check(flow.CompleteTest("Test1,5,OK;1;end").IsPass == true, "valid compact result remains compatible");
Check(adapter.ParseResults("Test1,1-2-0^3-4-0,NG;1;end").Count == 1, "short-circuit result remains compatible");
Check(flow.AcceptBarcode("NoRead", new TestFlowOptions()).Message.Contains("NoRead"), "scanner NoRead has explicit reason");
Check(flow.AcceptBarcode("123", new TestFlowOptions()).Message.Contains("长度"), "barcode rule failure is distinct from timeout");
Console.WriteLine($"Completed {checks} checks.");
