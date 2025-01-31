# Connecting PLC using Modbus and Dapr to Azure IoT Operations

## Overview
![Edge Device](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/clp-edgedevice.png?raw=true "Edge Device")

This Jumpstart Drop provides an example of a complete application that allows the connection of a PLC (Programmable Logic Controller) device, widely used by the industry to control and monitor machines, to [Azure IoT Operations](https://azure.microsoft.com/en-us/products/iot-operations?wt.mc_id=AZ-MVP-5003638) through an application written in C# [.net 9](https://dotnet.microsoft.com/pt-br/download/dotnet/9.0?wt.mc_id=AZ-MVP-5003638) that makes use of the Modbus.NET library to communicate with the PLC through a USB serial connection, and then forward the state of the registers that monitor the logic gates, to the Azure IoT Operations MQTT broker with the help of the [Dapr.io](https://dapr.io/?wt.mc_id=AZ-MVP-5003638) runtime and its integration in the pubsub standard.

## Prerequisites
1. Edge device server running Ubuntu 24.04 or latter
2. PLC ProXSys model CP-WS11
3. Azure IoT Operations install in the edge device

## Architecture
![Architecture](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/arquitetura.png?raw=true "Architecture")

- The PLC represents an industrial device that you want to monitor or control
- Using the Modbus RTU protocol through the USB serial interface, it connects to the Edge device running the Azure Arc Kubernetes cluster (in this case via the USB port)
- An application in .NET 9, packaged as a Docker container, is published in Azure Arc Kubernetes, in the Azure IoT Operations namespace, and uses the Dapr runtime to connect to the MQTT broker. Likewise, this application uses the [Modbus.Net](https://www.nuget.org/packages/Modbus.Net) library to connect to the PLC and collect data from the loggers.
- In Azure IoT Operations, we use its MQTT broker to receive messages and its Flow engine to forward the data for processing.


## PLC

![PLC ProXSys model CP-WS11](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/clp.png?raw=true "CLP")

The PLC used is a [ProXSys model CP-WS11](http://www.cpwscontroladores.com.br/Manuais/mapa_memoria_cpws11_4do4di_usb_V_1_5.pdf) and is a simple device for use as a testing and learning tool. It has a set of two digital inputs and two relays that can be controlled. It is programmed through proprietary software using the LADDER language. Two operations OP01 and OP02 were programmed to read the status of the input ports and write the status of these ports to registers 100002 and 100003. The logical device address in the Modbus protocol is 1. When connecting to the edge device, a serial connection is created in the /dev/ttyACM0 port at a speed of 9600 baud per second.

## Installing Azure IoT Operations
In this article [Installing Azure IoT Operations on an Ubuntu 24.04 Edge Server](https://www.linkedin.com/pulse/instalando-o-azure-iot-operations-em-um-edge-server-ubuntu-coan-8amyf/?trackingId=BzyCxAPVRymdG1tzqci%2B2A%3D%3D) I describe one of the possible processes for installing Azure IoT Operations on an edge device running a Kubernetes cluster. Next, you will need to enable the cluster in Azure Arc and finally install Azure IoT Operations.

## Installing Dapr on Azure IoT Operations
To simplify the development of the solution, the [Dapr Runtime](https://dapr.io/) was used in Azure IoT Operations, following this tutorial [this tutorial](https://learn.microsoft.com/en-us/azure/iot-operations/create-edge-apps/howto-deploy-dapr?wt.mc_id=AZ-MVP-5003638) that initially installs the Dapr operators in the Kubernetes cluster.
![Dapr Runtime](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/dapr-iotops-001.png?raw=true "Dapr Runtime")
And then two components that will allow the application to communicate with both the MQTT broker through the pubsub abstraction and through the state store abstraction.
![Dapr Components](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/dapr-iotops-002.png?raw=true "Dapr Components")
The next step is to build the .net application using the Dapr libraries to publish data through the pubsub abstraction in a fully decoupled way.

## .net 9 application with Dapr runtime
To start developing the application, a new .net 9 application was created using the Console model.
```
dotnet new console -o aio-modbus
```
The following packages were installed via Nuget:
```
dotnet add package Dapr.Client --version 1.14.0
dotnet add package Modbus.Net --version 1.4.3
dotnet add package Modbus.Net.Modbus --version 1.4.3    
dotnet add package Newtonsoft.Json --version 13.0.3
dotnet add package System.IO.Ports --version 9.0.1
```
To simplify the example, all the logic is concentrated in the Program.cs file.
1. An instance of Dapr Client is created, which will be responsible for connecting to the sidecar to establish the connection with the Azure IoT Operations MQTT Broker.
```
Console.WriteLine("AIO-MODBUS - CONNECT DAPR");
using var client = new DaprClientBuilder().Build();
```
2. Using the Modbus.Net library, we establish a serial RTU connection with the Modbus protocol on the /dev/ttyACM0 port. We describe the addresses of the PLC registers, which in this case is address 100002, where the Area = "1X" parameter represents the first two bytes of the address (10) and Address = 2 represents the rest of the address (0002).
```
Console.WriteLine("AIO-MODBUS - CONNECT MODBUS");
List<AddressUnit> addressUnits = new List<AddressUnit>
{
    new AddressUnit() {Id = "D1", Name="Variable 1", Area = "1X", Address = 2, CommunicationTag = "D1", DataType = typeof(byte)}
};
var  machine = new ModbusMachine("CLP",ModbusType.Rtu, "/dev/ttyACM0", addressUnits, true, 1, 3, Endian.BigEndianLsb);
```
3. We register a Scheduler, which every 5 seconds will query all the PLC register addresses, collect their state, and create an instance of the CLPData class that will then be published to the Dapr pubsub service.
```
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
```
The Modbus.net library requires an appsettings.default.json file containing the parameterization of the serial communication that will be performed.
```
{
  "Modbus.Net": {
    "COM": {
      "FetchSleepTime": "100",
      "ConnectionTimeout": "5000",
      "BaudRate": "BaudRate9600",
      "Parity": "Odd",
      "StopBits": "One",
      "DataBits": "Eight",
      "Handshake": "None",
      "FullDuplex": "False"
    },
    "Controller": {
      "WaitingListCount": "100"
    }
  }
}

```

## Application Build

To build the application, a Dockerfile was created, which uses the multi-stage strategy to first compile the project and then generate the final image. It is important to note that in order for the application to have access to the serial port on the device, the strategy of executing the process as a root user was used. This strategy can be avoided by creating a specific user with read and write permissions on the serial port.
```
FROM mcr.microsoft.com/dotnet/sdk:9.0@sha256:3fcf6f1e809c0553f9feb222369f58749af314af6f063f389cbd2f913b4ad556 AS build
WORKDIR /App

COPY . ./
RUN dotnet restore
RUN dotnet publish -o out
COPY ./appsettings.* ./out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0@sha256:b4bea3a52a0a77317fa93c5bbdb076623f81e3e2f201078d89914da71318b5d8
WORKDIR /App
COPY --from=build /App/out .
USER root

ENTRYPOINT ["dotnet", "aio-modbus.dll"]
```
Once the Docker image has been built and pushed to a container registry, the next step is to deploy the application to the edge device.

## Application Deployment

For deployment, a YAML file was created to deploy to the cluster, [similar to the one described in this tutorial](https://learn.microsoft.com/en-us/azure/iot-operations/create-edge-apps/howto-develop-dapr-apps?wt.mc_id=AZ-MVP-5003638) with the main change being the need to mount the volume pointing to the serial port /dev/ttyACM0 and giving elevated privilege permission: privileged: true

```
apiVersion: v1
kind: ServiceAccount
metadata:
  name: dapr-client
  namespace: azure-iot-operations
  annotations:
    aio-broker-auth/group: dapr-workload
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: my-dapr-app
  namespace: azure-iot-operations
spec:
  selector:
    matchLabels:
      app: my-dapr-app
  template:
    metadata:
      labels:
        app: my-dapr-app
      annotations:
        dapr.io/enabled: "true"
        dapr.io/inject-pluggable-components: "true"
        dapr.io/app-id: "my-dapr-app"
    spec:
      serviceAccountName: dapr-client

      volumes:
      # SAT used to authenticate between Dapr and the MQTT broker
      - name: mqtt-client-token
        projected:
          sources:
            - serviceAccountToken:
                path: mqtt-client-token
                audience: aio-internal
                expirationSeconds: 86400

      # Certificate chain for Dapr to validate the MQTT broker
      - name: aio-ca-trust-bundle
        configMap:
          name: azure-iot-operations-aio-ca-trust-bundle
      - name: tty
        hostPath:
          path: /dev/ttyACM0
      containers:
      # Container for the Dapr application
      - name: mq-dapr-app
        image: waltercoan/aio-modbus:latest
        volumeMounts:
          - name: tty
            mountPath: /dev/ttyACM0
        securityContext:
          privileged: true
```

This image shows the execution of the command that publishes the application and queries the pod execution logs within the Kubernetes cluster.

![kubectl apply](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/kubectlapply.png?raw=true "kubectlapply")

This image illustrates a client connected to the Azure IoT Operations MQTT broker consuming the messages generated by the application that monitors the PLC.

![MQTTX Client](https://github.com/waltercoan/reactor2025-azureiotoperations-modbus-dapr/blob/main/diagramas/mqttx-client.png?raw=true "MQTTX Client")

