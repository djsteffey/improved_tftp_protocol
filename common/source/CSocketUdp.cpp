#include "CSocketUdp.hpp"

namespace djs
{
	CSocketUdp::CSocketUdp()
	{

	}

	CSocketUdp::~CSocketUdp()
	{
		// make sure the socket is closed
		this->close();
	}

	bool CSocketUdp::initialize(unsigned short local_port)
	{
		if (this->m_socket.bind(local_port) != sf::Socket::Status::Done)
		{
			return false;
		}
		this->m_selector.add(this->m_socket);
		return true;
	}

	void CSocketUdp::set_remote(std::string remote_name, unsigned short remote_port)
	{
		this->m_remote_address = sf::IpAddress(remote_name);
		this->m_remote_port = remote_port;
	}

	bool CSocketUdp::send(char* buffer, int length)
	{
		if (this->m_socket.send(buffer, length, this->m_remote_address, this->m_remote_port) != sf::Socket::Done)
		{
			return false;
		}
		return true;
	}

	size_t CSocketUdp::receive(char* buffer, int length, std::string& sender_address, unsigned short& sender_port)
	{
		if (this->m_selector.wait(this->m_selector_timeout))
		{
			// something ready to receive
			size_t amount_received;
			sf::IpAddress address;
			if (this->m_socket.receive(buffer, length, amount_received, address, sender_port) != sf::Socket::Done)
			{
				return -1;
			}
			else
			{
				return amount_received;
			}
		}
		// gets here after the timeout if no socket
		return -1;
	}

	void CSocketUdp::close()
	{
		this->m_selector.clear();
		this->m_socket.unbind();
	}

	void CSocketUdp::set_timeout(int microseconds)
	{
		this->m_selector_timeout = sf::Time(sf::microseconds(microseconds));
	}

	std::string CSocketUdp::get_remote_ip()
	{
		return this->m_remote_address.toString();
	}
	
	unsigned short CSocketUdp::get_remote_port()
	{
		return this->m_remote_port;
	}
}
