#ifndef CSocketUdp_hpp
#define CSocketUdp_hpp

#include <SFML/Network.hpp>

namespace djs
{
	class CSocketUdp
	{
	public:
		CSocketUdp();
		~CSocketUdp();
		bool initialize(unsigned short local_port = 0);
		void set_remote(std::string remote_name, unsigned short remote_port);
		bool send(char* buffer, int length);
		size_t receive(char* buffer, int length, std::string& sender_address, unsigned short& sender_port);
		void close();
		void set_timeout(int microseconds);

		std::string get_remote_ip();
		unsigned short get_remote_port();
		
	protected:

	private:
		sf::UdpSocket m_socket;
		sf::SocketSelector m_selector;
		sf::Time m_selector_timeout;
		sf::IpAddress m_remote_address;
		unsigned short m_remote_port;
	};
}

#endif