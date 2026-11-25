using System.Globalization;
using System.IO;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;

namespace CH_V03.App.Communication;

internal sealed class AwinicForceTest(AwinicUsb board, bool version3)
{
    private static readonly double[] Gains = [1.1,2.2,3.3,4.4,5.5,6.6,7.7,8.8,11,13.2,14.3,15.4,16.5,17.6,18.7,19.8];
    internal static IReadOnlyList<string> Names(bool version3) => version3 ? Yidong2Exporter.Version3Columns : Yidong2Exporter.Columns;

    internal async Task<List<TestItemResult>> RunAsync(Action<string> stage, Action<TestItemResult> measured, CancellationToken cancellationToken = default)
    {
        Task<uint> ReadAsync(ushort address) => board.ReadAsync(address, cancellationToken);
        Task WriteAsync(ushort address, uint value) => board.WriteAsync(address, value, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var configPath = Path.Combine(Yidong2Exporter.InstallationDirectory(version3), "force_test_config.ini");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"请先在奕东软件中保存测试参数，缺少配置文件：{configPath}", configPath);
        var names = Names(version3);
        var settings = File.ReadAllLines(configPath).Select(line => line.Split('=',2))
            .Where(pair => pair.Length==2).ToDictionary(pair=>pair[0].Trim(),pair=>pair[1].Trim(),StringComparer.OrdinalIgnoreCase);
        double Number(string key) => double.Parse(settings[key],CultureInfo.InvariantCulture);
        int channel = int.Parse(settings["channel"]), samples=int.Parse(settings["samples"]), delay=int.Parse(settings["interval"]);
        if (channel is <0 or >4 || samples is <1 or >65535 || delay is <0 or >65535)
            throw new InvalidDataException("陪测配置的通道、采样次数或间隔无效");
        var areaFactor=Number("epsilon_0")*Number("epsilon_r")*Number("plane_area");
        var initial=Number("initial_d0");
        var rows=new List<TestItemResult>();
        stage("短路检测");
        var saved=await ReadAsync(0xfff4);
        uint high,low;
        try
        {
            await WriteAsync(0xf008,2);
            await WriteAsync(0xfff4,saved|0x3c00d103);
            await WriteAsync(0xc100,0x555555);
            await WriteAsync(0xe004,0);
            await WriteAsync(0xe00c,0xfff);
            await WriteAsync(0xe010,0xfff);
            await WriteAsync(0xe014,0);
            high=await ReadAsync(0xe008);
            await WriteAsync(0xe010,0);
            await WriteAsync(0xe014,0xfff);
            low=await ReadAsync(0xe008);
        }
        finally
        {
            if (board.Connected && !cancellationToken.IsCancellationRequested)
            {
                await WriteAsync(0xe010,0);
                await WriteAsync(0xe014,0);
                await WriteAsync(0xc100,0);
                await WriteAsync(0xfff4,saved|0x3c000000);
                await WriteAsync(0xf008,1);
            }
        }
        bool shortPass=high==0xfff && low==0;
        rows.Add(new(){Index=1,ItemName=names[0],TestValue=$"0x{high:X}/0x{low:X}",Result=shortPass?"PASS":"FAIL"});
        measured(rows[^1]);
        await Task.Delay(500, cancellationToken);
        if (version3)
        {
            stage("自动校准");
            var calibration = await ReadAsync(0x0004);
            await WriteAsync(0x0004, calibration | 0xfff);
            await Task.Delay(500, cancellationToken);
        }
        stage("读取通道偏移");
        int offset=0;
        double scale=0;
        for(int ch=0;ch<5;ch++)
        {
            int stride=ch*0x1b0;
            await ReadAsync((ushort)(0x168+stride));
            var bits=await ReadAsync((ushort)(0x174+stride));
            var gain=await ReadAsync((ushort)(0x170+stride));
            int raw=(int)((bits>>16)&255)*152;
            for(int bit=0;bit<8;bit++)
                if((bits&(1u<<(bit+8)))!=0) raw+=((bits&(1u<<bit))==0?9900:-9900)*(1<<bit);
            if (version3) Record(7 + ch, Math.Abs(raw) / 10000.0);
            if(ch!=channel) continue;
            scale=Gains[(gain>>12)&15]*10000/2097152;
            offset=(int)(Math.Abs(raw)/scale);
        }
        if (version3)
        {
            var gain = await ReadAsync((ushort)(0x170 + channel * 0x1b0));
            scale = Gains[(gain >> 12) & 15] * 10000 / 2097152;
        }
        double sum=0, absoluteSum=0;
        for(int group=1;group<=5;group++)
        {
            stage($"GAP{group} 采样");
            long total=0;
            for(int i=0;i<samples;i++)
            {
                total+=unchecked((int)await ReadAsync((ushort)(0x250+channel*0x1b0)))>>10;
                if(i+1<samples && delay>0) await Task.Delay(delay, cancellationToken);
            }
            double capacitance=(total/samples+offset)*scale/10000;
            var absolute = capacitance > 0 ? areaFactor / capacitance : 0;
            var gap=capacitance>0?absolute-initial:0;
            sum+=gap;
            absoluteSum += absolute;
            AddGap(group,gap,absolute,"lower"+group,"upper"+group);
        }
        AddGap(6,sum/5,absoluteSum/5,"avg_lower","avg_upper");
        return rows.OrderBy(row => row.Index).ToList();

        void AddGap(int index,double relative,double absolute,string lowerKey,string upperKey)
        {
            double value = version3 ? absolute : relative;
            string lower=settings[lowerKey],upper=settings[upperKey];
            double min=lower==""?0:Number(lowerKey), max=upper==""?0:Number(upperKey);
            bool pass=(min==0 && max==0) || (value>=min && value<=max);
            if (version3) Record(index, relative);
            Record(version3 ? index + 11 : index, value, pass, lower, upper);
        }

        void Record(int index, double value, bool pass = true, string lower = "", string upper = "")
        {
            rows.Add(new(){Index=index+1,ItemName=names[index],TestValue=value.ToString("G12",CultureInfo.InvariantCulture),
                UpperLimit=upper,LowerLimit=lower,Result=pass?"PASS":"FAIL"});
            measured(rows[^1]);
        }
    }
}
