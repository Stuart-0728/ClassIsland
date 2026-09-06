// bashu-rtc handles authenticated, audio-only WebRTC sessions over a private stdio pipe.
package main

import (
	"bufio"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"os"
	"strings"
	"sync"
	"time"

	"github.com/pion/interceptor"
	"github.com/pion/rtp/codecs"
	"github.com/pion/webrtc/v4"
	"github.com/pion/webrtc/v4/pkg/media/samplebuilder"
)

var outputLock sync.Mutex

func emit(value any) {
	outputLock.Lock()
	defer outputLock.Unlock()
	_ = json.NewEncoder(os.Stdout).Encode(value)
}
func run() error {
	scanner := bufio.NewScanner(os.Stdin)
	scanner.Buffer(make([]byte, 4096), 65536)
	if !scanner.Scan() {
		return fmt.Errorf("missing offer")
	}
	var input struct {
		Offer           string                    `json:"offer"`
		OfferCandidates []webrtc.ICECandidateInit `json:"offerCandidates"`
		IceServers      []webrtc.ICEServer        `json:"iceServers"`
		Relay           bool                      `json:"relay"`
	}
	if err := json.Unmarshal(scanner.Bytes(), &input); err != nil {
		return err
	}
	if len(input.Offer) > 32000 || !strings.HasPrefix(input.Offer, "v=0") {
		return fmt.Errorf("invalid offer")
	}
	engine := &webrtc.MediaEngine{}
	if err := engine.RegisterCodec(webrtc.RTPCodecParameters{
		RTPCodecCapability: webrtc.RTPCodecCapability{MimeType: webrtc.MimeTypeOpus, ClockRate: 48000, Channels: 2, SDPFmtpLine: "minptime=10;useinbandfec=1;stereo=0"},
		PayloadType:        111,
	}, webrtc.RTPCodecTypeAudio); err != nil {
		return err
	}
	interceptors := &interceptor.Registry{}
	if err := webrtc.RegisterDefaultInterceptors(engine, interceptors); err != nil {
		return err
	}
	configuration := webrtc.Configuration{ICEServers: input.IceServers}
	if input.Relay {
		configuration.ICETransportPolicy = webrtc.ICETransportPolicyRelay
	}
	settings := webrtc.SettingEngine{}
	// Pion's five-second disconnected default is too aggressive for school
	// Wi-Fi and cellular TURN paths. Keep ICE consent checks frequent while
	// allowing short routing gaps to recover without dropping live audio.
	settings.SetICETimeouts(30*time.Second, 60*time.Second, 2*time.Second)
	pc, err := webrtc.NewAPI(webrtc.WithMediaEngine(engine), webrtc.WithInterceptorRegistry(interceptors), webrtc.WithSettingEngine(settings)).
		NewPeerConnection(configuration)
	if err != nil {
		return err
	}
	defer pc.Close()
	pc.OnConnectionStateChange(func(state webrtc.PeerConnectionState) {
		emit(map[string]any{"type": "state", "state": state.String()})
	})
	pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
		if candidate != nil {
			emit(map[string]any{"type": "candidate", "candidate": candidate.ToJSON()})
		}
	})
	pc.OnTrack(func(track *webrtc.TrackRemote, _ *webrtc.RTPReceiver) {
		if !strings.EqualFold(track.Codec().MimeType, webrtc.MimeTypeOpus) {
			return
		}
		// Reorder a small window of packets. Late packets are discarded rather than accumulating latency.
		builder := samplebuilder.New(3, &codecs.OpusPacket{}, 48000)
		for {
			packet, _, err := track.ReadRTP()
			if err != nil {
				return
			}
			// Padding-only packets carry no Opus frame. Passing an empty frame to
			// the decoder would surface as OPUS_INVALID_PACKET and previously
			// caused the desktop client to abandon the realtime channel.
			if len(packet.Payload) == 0 {
				continue
			}
			builder.Push(packet)
			for sample := builder.Pop(); sample != nil; sample = builder.Pop() {
				if len(sample.Data) > 0 && len(sample.Data) <= 4096 {
					emit(map[string]any{"type": "audio", "data": base64.StdEncoding.EncodeToString(sample.Data)})
				}
			}
		}
	})
	if err = pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeOffer, SDP: input.Offer}); err != nil {
		return err
	}
	for _, candidate := range input.OfferCandidates {
		if err = pc.AddICECandidate(candidate); err != nil {
			return err
		}
	}
	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return err
	}
	if err = pc.SetLocalDescription(answer); err != nil {
		return err
	}
	// Trickle candidates through the parent instead of delaying the SDP until
	// TURN/TCP gathering completes.
	emit(map[string]any{"type": "answer", "sdp": pc.LocalDescription().SDP})
	done := make(chan struct{})
	go func() {
		for scanner.Scan() {
			if scanner.Text() == "stop" {
				break
			}
			var command struct {
				Candidate *webrtc.ICECandidateInit `json:"candidate"`
			}
			if json.Unmarshal(scanner.Bytes(), &command) == nil && command.Candidate != nil {
				if err := pc.AddICECandidate(*command.Candidate); err != nil {
					emit(map[string]any{"type": "error", "message": "invalid remote candidate"})
					break
				}
			}
		}
		close(done)
	}()
	select {
	case <-done:
	case <-time.After(20 * time.Minute):
	}
	return nil
}
func main() {
	if err := run(); err != nil {
		emit(map[string]any{"type": "error", "message": err.Error()})
		os.Exit(1)
	}
}
