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
		Offer      string             `json:"offer"`
		IceServers []webrtc.ICEServer `json:"iceServers"`
		Relay      bool               `json:"relay"`
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
	pc, err := webrtc.NewAPI(webrtc.WithMediaEngine(engine), webrtc.WithInterceptorRegistry(interceptors)).
		NewPeerConnection(configuration)
	if err != nil {
		return err
	}
	defer pc.Close()
	pc.OnConnectionStateChange(func(state webrtc.PeerConnectionState) {
		emit(map[string]any{"type": "state", "state": state.String()})
	})
	pc.OnTrack(func(track *webrtc.TrackRemote, _ *webrtc.RTPReceiver) {
		if !strings.EqualFold(track.Codec().MimeType, webrtc.MimeTypeOpus) {
			return
		}
		// Reorder a small window of packets. Late packets are discarded rather than accumulating latency.
		builder := samplebuilder.New(15, &codecs.OpusPacket{}, 48000)
		for {
			packet, _, err := track.ReadRTP()
			if err != nil {
				return
			}
			builder.Push(packet)
			for sample := builder.Pop(); sample != nil; sample = builder.Pop() {
				if len(sample.Data) <= 4096 {
					emit(map[string]any{"type": "audio", "data": base64.StdEncoding.EncodeToString(sample.Data)})
				}
			}
		}
	})
	if err = pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeOffer, SDP: input.Offer}); err != nil {
		return err
	}
	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		return err
	}
	gathered := webrtc.GatheringCompletePromise(pc)
	if err = pc.SetLocalDescription(answer); err != nil {
		return err
	}
	select {
	case <-gathered:
	case <-time.After(1 * time.Second):
	}
	emit(map[string]any{"type": "answer", "sdp": pc.LocalDescription().SDP})
	done := make(chan struct{})
	go func() {
		for scanner.Scan() {
			if scanner.Text() == "stop" {
				break
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
