#include "CTFTPClient.hpp"

namespace djs
{
	CTFTPClient::CTFTPClient()
	{

	}

	CTFTPClient::~CTFTPClient()
	{
		this->m_socket.close();
	}

	bool CTFTPClient::initialize(unsigned short local_port)
	{
		// initialize the socket
		if (this->m_socket.initialize(local_port) == false)
		{
			return false;
		}

		// have a default timeout of 5 seconds
		this->m_socket.set_timeout(5000000);
		return false;
	}
	
	bool CTFTPClient::send_file(std::string server_name, unsigned short server_port, std::string filename)
	{
		// set the destination
		this->m_socket.set_remote(server_name, server_port);

		// build tftp specific packets and send/receive them

		return false;
	}
	
	bool CTFTPClient::get_file(std::string server_name, unsigned short server_port, std::string filename)
	{
		// set the destination
		this->m_socket.set_remote(server_name, server_port);

		// build tftp specific packets and send/receive them

		return false;
	}
}