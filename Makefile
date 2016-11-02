all: sprog cprog

sprog: common/code/CSocketUdp.cs common/code/CTFTPMessage.cs common/code/CTFTPNode.cs server/code/CTFTPServer.cs server/code/Program.cs
	mcs common/code/CSocketUdp.cs common/code/CTFTPMessage.cs common/code/CTFTPNode.cs server/code/CTFTPServer.cs server/code/Program.cs -o sprog

cprog: common/code/CSocketUdp.cs common/code/CTFTPMessage.cs common/code/CTFTPNode.cs server/code/CTFTPServer.cs server/code/Program.cs
	mcs common/code/CSocketUdp.cs common/code/CTFTPMessage.cs common/code/CTFTPNode.cs client/code/Program.cs -o cprog

clean:
	rm cprog
	rm sprog
	rm data.csv