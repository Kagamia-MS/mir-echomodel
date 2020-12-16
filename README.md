# mir-echomodel

This project is used for stability test of containers and API gateway.

It provide functions to simulate crashloopbackoff, oom, request queueing, echo with delay, chunk streaming, upstream disconnected, etc.

## Build

build and run locally
```
cd src
dotnet build && dotnet run
```

or build as docker image
```
docker build --tag helloworldservice:latest .
```

## Command Line Options

```
dotnet HelloWorldService.dll --urls=http://0.0.0.0:5000/;http://0.0.0.0:5001/ --crash=true --oom=true --IsSingleThread=true --StartDelay=5
```

## Api

```
GET /score?time=50&size=1024&chunk=1&statusCode=200&abort=1
GET /kill?time=10000
GET /trace
GET /alloc?size=1024&count=1
GET /healthz
```

## Contributing

## Acknowledgement

## License
