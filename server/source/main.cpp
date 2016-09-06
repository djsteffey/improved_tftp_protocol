int main(int argc, char* argv[])
{
	return 0;
}

/*

#include "CServer.hpp"
#include <iostream>
#include <string>

std::string help_string(std::string program_name)
{
	// print out a usage string
	std::string s = "Usage: \n";
	s += program_name + " port filename\n";
	s += "	port - listening port of server\n";
	s += "	filename - filename to store messages\n";
	return s;
}

int main(int argc, char* argv[])
{
	// first set the program name as it will always be there
	std::string program_name = argv[0];

	// check number of arguments to ensure correct (1 program name + 2 args = 3)
	if (argc != 3)
	{
		std::cout << "***ERROR*** Invalid number of arguments" << std::endl;
		std::cout << help_string(program_name) << std::endl;
		return -1;
	}

	// validate input
	// argv[1] - convert the port to an actual number...if possible
	int server_port = 0;
	try
	{
		server_port = std::stoi(argv[1]);
	}
	catch (...)
	{
		std::cout << "***ERROR*** Invalid argument for port" << std::endl;
		std::cout << help_string(program_name) << std::endl;
		return -1;
	}
	
	// argv[2] - store the filename
	std::string filename = argv[2];

	// now construct the server object and run it
	djs::CServer server(server_port, filename);
	if (server.run() == false)
	{
		return -2;
	}
	return 0;
}
*/