#include <iostream>
#include "CTFTPClient.hpp"

#ifdef _WIN32
	#pragma comment(lib, "sfml-system-d.lib")
	#pragma comment(lib, "sfml-network-d.lib")
#endif

int main(int argc, char* argv[])
{
	djs::CTFTPClient client;
	client.initialize();
	client.get_file("127.0.0.1", 12345, "test_file.txt");


	getchar();
}

/*
#include "CClient.hpp"
#include <iostream>
#include <string>

std::string help_string(std::string program_name)
{
	// print out a help statement
	std::string s = "Usage: \n";
	s += program_name + " server port file1 file2\n";
	s += "	server - server name or ip address\n";
	s += "	port - listening port of server\n";
	s += "	file1 - filename containing messages to encode in CRC32\n";
	s += "	file2 - filename containing message to send to server";
	return s;
}

int main(int argc, char* argv[])
{
	// first set the program name as it will always be there
	std::string program_name = argv[0];

	// check number of arguments to ensure correct (1 program name + 4 args = 5)
	if (argc != 5)
	{
		std::cout << "***ERROR*** Invalid number of arguments" << std::endl;
		std::cout << help_string(program_name) << std::endl;
		return -1;
	}

	// validate input
	// argv[1] - store the server name
	std::string server_name(argv[1]);

	// argv[2] - convert the port to an actual number...if possible
	int server_port = 0;
	try
	{
		server_port = std::stoi(argv[2]);
	}
	catch (...)
	{
		std::cout << "***ERROR*** Invalid argument for port" << std::endl;
		std::cout << help_string(program_name) << std::endl;
		return -2;
	}
	
	// argv[3] - store the file1 name
	std::string filename_1 = argv[3];
	
	// argv[4] - store the file2 name
	std::string filename_2 = argv[4];

	// now construct the client object and run it
	djs::CClient client(server_name, server_port, filename_1, filename_2);
	if (client.run(1) == false)
	{
		return -3;
	}
	return 0;
}
*/