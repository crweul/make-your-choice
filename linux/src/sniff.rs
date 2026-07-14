use pnet::datalink::{self, Channel::Ethernet};
use pnet::packet::ethernet::{EthernetPacket, EtherTypes};
use pnet::packet::ipv4::Ipv4Packet;
use pnet::packet::udp::UdpPacket;
use pnet::packet::Packet;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;

pub struct TrafficSniffer {
    running: Arc<AtomicBool>,
}

impl TrafficSniffer {
    pub fn new<F>(callback: F) -> Self
    where F: Fn(String, u16, u16) + Send + 'static + Sync
    {
        let running = Arc::new(AtomicBool::new(true));
        
        // Spawn sniffing thread
        let running_clone = running.clone();
        thread::spawn(move || {
            Self::sniff(running_clone, callback);
        });

        Self {
            running,
        }
    }

    fn sniff<F>(running: Arc<AtomicBool>, callback: F)
    where F: Fn(String, u16, u16)
    {
        let interfaces = datalink::interfaces();
        let interface = interfaces
            .into_iter()
            .find(|iface| iface.is_up() && !iface.is_loopback() && !iface.ips.is_empty());

        let interface = match interface {
            Some(i) => i,
            None => {
                eprintln!("Sniffer: No suitable network interface found.");
                return;
            }
        };

        let (_, mut rx) = match datalink::channel(&interface, Default::default()) {
            Ok(Ethernet(tx, rx)) => (tx, rx),
            Ok(_) => {
                eprintln!("Sniffer: Unhandled channel type or error.");
                return;
            }
            Err(e) => {
                eprintln!("Sniffer: Failed to create channel: {}", e);
                return;
            }
        };

        while running.load(Ordering::Relaxed) {
            if let Ok(packet) = rx.next() {
                if let Some(packet) = EthernetPacket::new(packet) {
                    if packet.get_ethertype() == EtherTypes::Ipv4 {
                        if let Some(header) = Ipv4Packet::new(packet.payload()) {
                            if header.get_next_level_protocol()
                                == pnet::packet::ip::IpNextHeaderProtocols::Udp
                            {
                                if let Some(udp) = UdpPacket::new(header.payload()) {
                                    let src_port = udp.get_source();
                                    let dst_port = udp.get_destination();

                                    let src_in_range = src_port >= 7777 && src_port <= 7820;
                                    let dst_in_range = dst_port >= 7777 && dst_port <= 7820;

                                    if src_in_range || dst_in_range {
                                        let remote_ip = if src_in_range {
                                            header.get_source()
                                        } else {
                                            header.get_destination()
                                        };
                                        let port = if src_in_range { src_port } else { dst_port };
                                        // Our local port is the side NOT in the game range.
                                        let local_port = if src_in_range { dst_port } else { src_port };
                                        callback(remote_ip.to_string(), port, local_port);

                                        // Capture the game's own UE InitialConnect handshake (prefix
                                        // B8 01 02 80 00) from a real outbound packet, so the beacon's
                                        // probe template auto-updates to the current build's magic.
                                        // Skip our own beacon probes.
                                        if dst_in_range && !crate::live_probe::is_beacon_local_port(local_port) {
                                            let pl = udp.payload();
                                            if pl.len() >= 20
                                                && pl.len() <= 100
                                                && pl[0] == 0xB8
                                                && pl[1] == 0x01
                                                && pl[2] == 0x02
                                                && pl[3] == 0x80
                                                && pl[4] == 0x00
                                            {
                                                crate::live_probe::set_learned_handshake(pl);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    #[allow(dead_code)]
    pub fn stop(&self) {
        self.running.store(false, Ordering::Relaxed);
    }
}
