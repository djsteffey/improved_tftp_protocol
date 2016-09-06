#ifndef CTFTPClient_hpp
#define CTFTPClient_hpp

#include <string>
#include "CSocketUdp.hpp"

namespace djs
{
	class CTFTPClient
	{
	public:
		CTFTPClient();
		~CTFTPClient();
		bool initialize(unsigned short local_port = 0);
		bool send_file(std::string server_name, unsigned short server_port, std::string filename);
		bool get_file(std::string server_name, unsigned short server_port, std::string filename);

	protected:

	private:
		CSocketUdp m_socket;
	};
}

#endif
