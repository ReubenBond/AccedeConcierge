import React, { useEffect, ReactNode, RefObject, useState, useRef, ChangeEvent } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import remarkBreaks from 'remark-breaks';
import { Message, FileAttachment, CandidateItinerariesMessage } from '../types/ChatTypes';
import { TripOption } from '../types/TripTypes';

interface ChatContainerProps {
    messages: Message[];
    prompt: string;
    setPrompt: (prompt: string) => void;
    handleSubmit: (e: React.FormEvent) => void;
    cancelChat: () => void;
    streamingMessageId: string | null;
    messagesEndRef: RefObject<HTMLDivElement | null>;
    shouldAutoScroll: boolean;
    renderMessages: () => ReactNode;
    chatId: string;
    selectedFiles: File[];
    setSelectedFiles: (files: File[]) => void;
    selectItinerary?: (optionId: string) => void;
}

const ChatContainer: React.FC<ChatContainerProps> = ({
    messages,
    prompt,
    setPrompt,
    handleSubmit,
    cancelChat,
    streamingMessageId,
    messagesEndRef,
    shouldAutoScroll,
    selectedFiles,
    setSelectedFiles,
    selectItinerary
}: ChatContainerProps) => {
    const [copiedMsgId, setCopiedMsgId] = useState<string | null>(null);
    const [canScrollUp, setCanScrollUp] = useState<boolean>(false);
    const [canScrollDown, setCanScrollDown] = useState<boolean>(false);
    const containerRef = useRef<HTMLDivElement | null>(null);
    const fileInputRef = useRef<HTMLInputElement>(null);

    // Function to copy message text to clipboard
    const copyToClipboard = (text: string, msgId: string) => {
        navigator.clipboard.writeText(text).then(
            () => {
                setCopiedMsgId(msgId);
                // Reset copied state after 2 seconds
                setTimeout(() => setCopiedMsgId(null), 2000);
            },
            (err) => {
                console.error('Could not copy text: ', err);
            }
        );
    };

    // Function to handle itinerary selection
    const handleItinerarySelect = (optionId: string) => {
        if (selectItinerary) {
            selectItinerary(optionId);
        }
    };

    // Function to render a trip option
    const renderTripOption = (option: TripOption, index: number) => {
        return (
            <div key={option.optionId || index} className="trip-option">
                <h3>Option {index + 1}: {option.description}</h3>
                <div className="trip-option-details">
                    {/* Flights section */}
                    <div className="trip-section flights">
                        <h4>Flights</h4>
                        {option.flights.map((flight, flightIdx) => (
                            <div key={flightIdx} className="flight-item">
                                <p><strong>{flight.airline} {flight.flightNumber}</strong></p>
                                <p>{flight.origin} → {flight.destination}</p>
                                <p>{new Date(flight.departureTime).toLocaleString()} - {new Date(flight.arrivalTime).toLocaleString()}</p>
                                {flight.cabinClass && <p>Class: {flight.cabinClass}</p>}
                                <p>Price: ${flight.price}</p>
                            </div>
                        ))}
                    </div>
                    
                    {/* Hotel section if available */}
                    {option.hotel && (
                        <div className="trip-section hotel">
                            <h4>Hotel</h4>
                            <p><strong>{option.hotel.propertyName}</strong> ({option.hotel.chain})</p>
                            <p>{option.hotel.address}</p>
                            <p>{new Date(option.hotel.checkIn).toLocaleDateString()} to {new Date(option.hotel.checkOut).toLocaleDateString()}</p>
                            <p>{option.hotel.nightCount} nights, {option.hotel.roomType}</p>
                            <p>${option.hotel.pricePerNight}/night (Total: ${option.hotel.totalPrice})</p>
                            {option.hotel.breakfastIncluded && <p>Breakfast included</p>}
                        </div>
                    )}
                    
                    {/* Car rental section if available */}
                    {option.car && (
                        <div className="trip-section car-rental">
                            <h4>Car Rental</h4>
                            <p><strong>{option.car.company}</strong> - {option.car.carType}</p>
                            <p>Pick-up: {option.car.pickupLocation} at {new Date(option.car.pickupTime).toLocaleString()}</p>
                            <p>Drop-off: {option.car.dropoffLocation} at {new Date(option.car.dropoffTime).toLocaleString()}</p>
                            <p>${option.car.dailyRate}/day (Total: ${option.car.totalPrice})</p>
                            {option.car.unlimitedMileage && <p>Unlimited mileage</p>}
                        </div>
                    )}
                </div>
                <div className="trip-option-total">
                    <p><strong>Total Cost: ${option.totalCost}</strong></p>
                    <button 
                        className="select-option-button"
                        onClick={() => handleItinerarySelect(option.optionId)}
                    >
                        Select this itinerary
                    </button>
                </div>
            </div>
        );
    };
    
    // Function to render messages with special handling for candidate itineraries
    const renderMessages = () => {
        return messages.map(msg => {
            // Special handling for candidate itineraries message type
            if (msg.type === 'candidate-itineraries') {
                const candidateMsg = msg as CandidateItinerariesMessage;
                return (
                    <div 
                        key={msg.responseId} 
                        className={`message ${msg.role}`}
                        data-type={msg.type}
                    >
                        <div className="message-container">
                            <div className="message-content">
                                <ReactMarkdown 
                                    remarkPlugins={[remarkGfm, remarkBreaks]}
                                >
                                    {msg.text}
                                </ReactMarkdown>
                                
                                <div className="trip-options-container">
                                    {candidateMsg.options && candidateMsg.options.map((option, index) => 
                                        renderTripOption(option, index)
                                    )}
                                </div>
                                
                                {renderAttachments(msg.attachments)}
                                <button 
                                    className={`copy-message-button ${copiedMsgId === msg.responseId ? 'copied' : ''}`}
                                    onClick={() => copyToClipboard(msg.text, msg.responseId)}
                                    aria-label="Copy message"
                                    title="Copy to clipboard"
                                >
                                    {copiedMsgId === msg.responseId ? (
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <polyline points="20 6 9 17 4 12"></polyline>
                                        </svg>
                                    ) : (
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                                            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                                        </svg>
                                    )}
                                </button>
                            </div>
                        </div>
                    </div>
                );
            }
            
            // Default rendering for other message types
            return (
                <div 
                    key={msg.responseId} 
                    className={`message ${msg.role} ${msg.type === 'preference-updated' ? 'preference-message' : ''}`}
                    data-type={msg.type}
                >
                    <div className="message-container">
                        <div className="message-content">
                            <ReactMarkdown 
                                remarkPlugins={[remarkGfm, remarkBreaks]}
                            >
                                {msg.text}
                            </ReactMarkdown>
                            {renderAttachments(msg.attachments)}
                            {msg.type !== 'preference-updated' && (
                                <button 
                                    className={`copy-message-button ${copiedMsgId === msg.responseId ? 'copied' : ''}`}
                                    onClick={() => copyToClipboard(msg.text, msg.responseId)}
                                    aria-label="Copy message"
                                    title="Copy to clipboard"
                                >
                                    {copiedMsgId === msg.responseId ? (
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <polyline points="20 6 9 17 4 12"></polyline>
                                        </svg>
                                    ) : (
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                                            <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                                            <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                                        </svg>
                                    )}
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            );
        });
    };
    
    // Check if container can scroll and show/hide shadows accordingly
    const checkScroll = () => {
        if (messagesEndRef.current) {
            const { scrollTop, scrollHeight, clientHeight } = messagesEndRef.current;
            
            // Show top shadow if we're not at the top (more sensitive threshold)
            setCanScrollUp(scrollTop > 5);
            
            // Show bottom shadow if we're not at the bottom (more sensitive threshold)
            setCanScrollDown(scrollTop + clientHeight < scrollHeight - 5);
        }
    };

    // Handle file selection
    const handleFileSelect = (e: ChangeEvent<HTMLInputElement>) => {
        if (e.target.files && e.target.files.length > 0) {
            // Convert FileList to array and filter for only image files
            const fileArray = Array.from(e.target.files).filter(
                file => file.type.startsWith('image/')
            );
            
            if (fileArray.length > 0) {
                setSelectedFiles([...selectedFiles, ...fileArray]);
            }
        }
    };

    // Handle click on attachment button
    const handleAttachmentClick = () => {
        if (fileInputRef.current) {
            fileInputRef.current.click();
        }
    };

    // Remove file from selected files
    const removeSelectedFile = (indexToRemove: number) => {
        setSelectedFiles(selectedFiles.filter((_, index) => index !== indexToRemove));
    };

    // Scroll only if near the bottom
    useEffect(() => {
        if (shouldAutoScroll && messagesEndRef.current) {
            messagesEndRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [messages, shouldAutoScroll, messagesEndRef]);

    // Setup scroll listener
    useEffect(() => {
        const currentRef = messagesEndRef.current;
        
        if (currentRef) {
            // Initial check
            checkScroll();
            
            // Add scroll listener
            currentRef.addEventListener('scroll', checkScroll);
            
            // Check after content changes
            const observer = new MutationObserver(checkScroll);
            observer.observe(currentRef, { childList: true, subtree: true });
            
            return () => {
                currentRef.removeEventListener('scroll', checkScroll);
                observer.disconnect();
            };
        }
    }, []);

    // Check scroll state on content changes
    useEffect(() => {
        checkScroll();
    }, [messages]);

    // Render attachments in message
    const renderAttachments = (attachments?: FileAttachment[]) => {
        if (!attachments || attachments.length === 0) return null;
        
        return (
            <div className="message-attachments">
                {attachments.map((attachment, index) => (
                    <div key={index} className="message-image-attachment">
                        <a href={attachment.uri} target="_blank" rel="noopener noreferrer">
                            <img 
                                src={attachment.uri} 
                                className="attached-image"
                            />
                        </a>
                    </div>
                ))}
            </div>
        );
    };

    return (
        <div 
            ref={containerRef} 
            className={`chat-container ${canScrollUp ? 'can-scroll-up' : ''} ${canScrollDown ? 'can-scroll-down' : ''}`}
        >
            {/* Top shadow indicator */}
            <div className={`scroll-shadow-top ${canScrollUp ? 'visible' : ''}`} />
            
            <div ref={messagesEndRef} className="messages-container" onScroll={checkScroll}>
                {renderMessages()}
            </div>
            
            {/* Form container with positioned shadow */}
            <div className="form-container">
                {/* Bottom shadow indicator positioned above the form */}
                <div className={`scroll-shadow-bottom ${canScrollDown ? 'visible' : ''}`} />
                
                {/* Show selected file previews */}
                {selectedFiles.length > 0 && (
                    <div className="selected-files-container">
                        {selectedFiles.map((file, index) => (
                            <div key={index} className="selected-file-preview">
                                <img 
                                    src={URL.createObjectURL(file)} 
                                    alt={file.name}
                                    className="file-preview-thumbnail" 
                                />
                                <button 
                                    className="remove-file-button"
                                    onClick={() => removeSelectedFile(index)}
                                    aria-label="Remove file"
                                >
                                    ×
                                </button>
                            </div>
                        ))}
                    </div>
                )}
                
                <form onSubmit={handleSubmit} className="message-form" autoComplete="off">
                    {/* Hidden file input */}
                    <input
                        type="file"
                        ref={fileInputRef}
                        onChange={handleFileSelect}
                        style={{ display: 'none' }}
                        accept="image/*"
                        multiple
                    />
                    
                    {/* Attachment button */}
                    <button 
                        type="button" 
                        onClick={handleAttachmentClick}
                        disabled={streamingMessageId ? true : false}
                        className="attachment-button"
                        title="Attach image"
                    >
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"></path>
                        </svg>
                    </button>
                    
                    <input
                        type="text"
                        value={prompt}
                        onChange={e => setPrompt(e.target.value)}
                        placeholder="How can I help you with your travel plans?"
                        disabled={streamingMessageId ? true : false}
                        className="message-input"
                        autoComplete="off"
                        name="message-input"
                    />
                    {streamingMessageId ? (
                        <button type="button" onClick={cancelChat} className="message-button">
                            Stop
                        </button>
                    ) : (
                        <button type="submit" disabled={streamingMessageId ? true : false} className="message-button">
                            Send
                        </button>
                    )}
                </form>
            </div>
        </div>
    );
};

export default ChatContainer;
