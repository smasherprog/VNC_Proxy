VNC_Proxy
=========

VNC_Proxy / Repeater<br/>
<br/>
There are many terms used for this program: repeater; gateway; and proxy. What this program does is acts as a middle man between two computers who want to connect using Ultra VNC. If both computers are behind a router, they cannot directly connect to each other because they do not have routable IP addresses. So, this program MUST have a PUBLIC IP address accessable to both machines! The picture here is a good example http://www.uvnc.com/products/uvnc-repeater.html
<br/>
Currently this only works with Ultra VNC http://www.uvnc.com/<br/>
<br/>
Setup Instructions (You must supply your real IP addresses. I make up my own for the example below)<br/>
You should have three separate machines: Two Computers for VNC, one to view, one for servicing, and, a Proxy server.<br/>
<br/>
Get the IP address of the VNC_Proxy Machine. I will assume it is 206.1.1.1 (remember, this MUST be a public IP!)<br/>
Machine A IP 10.1.1.1 <br/>
Machine B IP 10.1.1.2<br/>
<br/>
goto http://www.uvnc.com/ download the latest version of UltraVNC and install on Machine A and B<br/>
Build and Run the VNC_Proxy program on the computer which is acting as the middle man and has a publicly routable IP!<br/><br/>
On Machine A start up VNC Server (it will be in Start -> Programs )<br/>
Once the program starts up, a yellow icon will be placed into your taskbar named WinVNC. <br/>
Right click on the icon and goto Add new Client. <br/>
Hostname should be the ip address of YOUR Proxy, i.e. 206.1.1.1<br/>
Connection # 1234               -- any number is fine here. The repeater uses this to pair up server and client<br/>
Press Ok.<br/>
Machine A should now be connected to the repeater waiting for Machine B<br/>
<br/>
On Machine B start up VNC Viewer<br/>
VNC Server: Textbox enter           id:1234<br/>
Check the box that says "Proxy/Repeater" and enter your repeaters IP address and port. The default port is 5901 <br/>
So, it would be 206.1.1.1:5901  <br/>
Press Connect<br/>

The two machines should now connect to each other through the proxy. Good Luck!<br/>
