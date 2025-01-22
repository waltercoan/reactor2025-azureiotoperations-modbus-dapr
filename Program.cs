using Dapr.Client;
using Modbus.Net;
using Modbus.Net.Modbus;
using Models;
using AddressUnit = Modbus.Net.AddressUnit<string, int, int>;
using MachineJobSchedulerCreator = Modbus.Net.MachineJobSchedulerCreator<Modbus.Net.IMachineMethodDatas, string, double>;
using ModbusMachine = Modbus.Net.Modbus.ModbusMachine<string, string>;

IEnumerable<CLPData> values = new List<CLPData>();

Console.WriteLine("AIO-MODBUS - V 0.39");
Console.WriteLine("AIO-MODBUS - START");

Console.WriteLine("AIO-MODBUS - CONNECT DAPR");
using var client = new DaprClientBuilder().Build();

Console.WriteLine("AIO-MODBUS - CONNECT MODBUS");
List<AddressUnit> addressUnits = new List<AddressUnit>
{
    new AddressUnit() {Id = "D1", Name="Variable 1", Area = "1X", Address = 2, CommunicationTag = "D1", DataType = typeof(byte)}
};
var  machine = new ModbusMachine("CLP",ModbusType.Rtu, "/dev/ttyACM0", addressUnits, true, 1, 3, Endian.BigEndianLsb);

Console.WriteLine("AIO-MODBUS - SENDING DATA");
await MachineJobSchedulerCreator.CreateScheduler("Trigger1", -1, 5).Result.From(machine.Id, machine, MachineDataType.CommunicationTag).Result.Query("Query1",
    returnValues =>
    {
        
        if (returnValues.ReturnValues.Datas != null)
        {            
            lock (values)
            {
                Console.WriteLine("AIO-MODBUS - PROCESS DATA");
                var unitValues = from val in returnValues.ReturnValues.Datas
                                select
                                new Tuple<AddressUnit, double?>(
                                    addressUnits.FirstOrDefault(p => p.CommunicationTag == val.Key)!, val.Value.DeviceValue);

                values = from unitValue in unitValues
                        select
                        new CLPData(
                            unitValue.Item1.Id,
                            unitValue.Item1.Name,
                            unitValue.Item1.Address + "." + unitValue.Item1.SubAddress,
                            unitValue.Item2 ?? 0,
                            unitValue.Item1.DataType.Name
                        );
                
            }
            Console.WriteLine("AIO-MODBUS - PUBLISH MQTT");
            values.ToList().ForEach(item => {
                    
                    var dumpjson = Newtonsoft.Json.JsonConvert.SerializeObject(item);
                    Console.WriteLine("AIO-MODBUS - returnValues: " + dumpjson);

                    client.PublishEventAsync("iotoperations-pubsub", "clp", item).Wait();    
                    Console.WriteLine("Published data: " + item);
                }
            );
            values.ToList().RemoveAll(p => true);
        }
        else
        {
            Console.WriteLine($"ip {returnValues.MachineId} not return value");
        }
        return null;
    }).Result.Run();
            
try
{
    while (true)
    {
        if(machine.IsConnected)
        {
            Console.WriteLine("AIO-MODBUS - CONNECTED...");
        }
        else
        {
            Console.WriteLine("AIO-MODBUS - NOT CONNECTED");
        }
        await Task.Delay(10000);
    }
}
finally
{
    Console.WriteLine("AIO-MODBUS - STOP");
}


